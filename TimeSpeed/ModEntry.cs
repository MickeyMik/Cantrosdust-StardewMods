using System;
using System.Collections.Generic;
using System.Linq;
using cantorsdust.Common;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using TimeSpeed.Framework;

namespace TimeSpeed;

/// <summary>The entry class called by SMAPI.</summary>
internal class ModEntry : Mod
{
    /*********
    ** Properties
    *********/
    /// <summary>Displays messages to the user.</summary>
    private readonly Notifier Notifier = new();

    /// <summary>Provides helper methods for tracking time flow.</summary>
    private readonly TimeHelper TimeHelper = new();

    /// <summary>The mod configuration.</summary>
    private ModConfig Config;

    /// <summary><c>True</c> if player has manually frozen time.</summary>
    private bool ManualFreeze;

    /// <summary>Whether the player has manually overridden <see cref="AutoFreezeReason.FrozenForLocation"/>.</summary>
    private bool FreezeOverrideLocation;

    /// <summary>Whether the player has manually overridden <see cref="AutoFreezeReason.FrozenAtTime"/>.</summary>
    private bool FreezeOverrideTime;

    /// <summary>Whether the player has manually overridden <see cref="AutoFreezeReason.FrozenBeforePassOut"/>.</summary>
    private bool FreezeOverridePassOut;

    /// <summary>The reason time would be frozen automatically if applicable, regardless of <see cref="ManualFreeze"/>.</summary>
    private AutoFreezeReason AutoFreeze = AutoFreezeReason.None;

    /// <summary>Whether time should be frozen.</summary>
    private bool IsTimeFrozen =>
        this.ManualFreeze == true
        || (this.AutoFreeze == AutoFreezeReason.FrozenForLocation && this.FreezeOverrideLocation == false)
        || (this.AutoFreeze == AutoFreezeReason.FrozenBeforePassOut && this.FreezeOverridePassOut == false)
        || (this.AutoFreeze == AutoFreezeReason.FrozenAtTime && this.FreezeOverrideTime == false);

    /// <summary>Whether the flow of time should be adjusted today (if it is a festival day).</summary>
    /// <remarks>Currently only enables/disables based on user setting for festival days</remarks>
    private bool IsTimeAdjustmentEnabledToday;

    /// <summary>Minimum milliseconds allowed for <see cref="TargetTickInterval"/>.</summary>
    /// <remarks>Could be added to ModConfig menu.</remarks>
    private int minTickIntervalAllowed = 100;

    /// <summary>Backing field for <see cref="TargetTickInterval"/>.</summary>
    private int _tickInterval;

    /// <summary>The number of milliseconds per 10-game-minute tick to apply.</summary>
    private int TargetTickInterval
    {
        get => this._tickInterval;
        set => this._tickInterval = Math.Max(value, this.minTickIntervalAllowed);
    }

    /// <summary>The number of milliseconds that has elapsed since last 10-game-minute tick interval.</summary>
    private double ElapsedTimeInCurrentTickInterval;

    /// <summary>The percentage of <see cref="TargetTickInterval"/> that has elapsed since the last tick.</summary>
    /// <remarks>See <see cref="ElapsedTimeInCurrentTickInterval"/> for milliseconds.</remarks>
    private double TargetTickProgress;

    /// <summary>Yet to be implemented: intended idea is for this to track which player has wasted most time, for "fair" mode (list players, location?, and time "wasted")</summary>
    private Dictionary<string, int> PlayerInterval;


    /*********
    ** Public methods
    *********/
    /// <inheritdoc />
    public override void Entry(IModHelper helper)
    {
        I18n.Init(helper.Translation);
        CommonHelper.RemoveObsoleteFiles(this, "TimeSpeed.pdb");

        // read config
        this.Config = helper.ReadConfig<ModConfig>();

        // add time events
        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        helper.Events.GameLoop.TimeChanged += this.OnTimeChanged;
        helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;
        //helper.Events.Player.Warped += this.OnWarped; // testing the tick updater check instead, see onUpdateTicked();
        helper.Events.Multiplayer.ModMessageReceived += this.OnModMessageReceived;

        // add time freeze/unfreeze notification
        {
            bool wasPaused = false;
            helper.Events.Display.RenderingHud += (_, _) =>
            {
                wasPaused = Game1.paused;
                if (this.IsTimeFrozen)
                    Game1.paused = true;
            };

            helper.Events.Display.RenderedHud += (_, _) =>
            {
                Game1.paused = wasPaused;
            };
        }
    }


    /*********
    ** Private methods
    *********/
    /****
    ** Event handlers
    ****/
    /// <inheritdoc cref="IGameLoopEvents.GameLaunched"/>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
    {
        GenericModConfigMenuIntegration.Register(this.ModManifest, this.Helper.ModRegistry, this.Monitor,
            getConfig: () => this.Config,
            reset: () => this.Config = new(),
            save: () =>
            {
                this.Helper.WriteConfig(this.Config);
                if (Context.IsWorldReady && this.ShouldEnable())
                    this.UpdateSettingsForLocation(Game1.currentLocation);
            }
        );
    }

    /// <inheritdoc cref="IGameLoopEvents.SaveLoaded"/>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
    {
        if (!Context.IsMainPlayer)
            // This should be changed now that there is multiplayer-interactivity. Host is only requirement, unless other players want notifications.
            this.Monitor.Log("Time functionality disabled; only host controls time in multiplayer, mod is optional for farmhands if they want to receive notifications.", LogLevel.Warn);
    }

    /// <inheritdoc cref="IGameLoopEvents.DayStarted"/>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnDayStarted(object sender, DayStartedEventArgs e)
    {
        if (!this.ShouldEnable())
            return;

        this.UpdateTimeAdjustmentEnabledToday(Game1.season, Game1.dayOfMonth);
        this.UpdateTimeFreeze(clearAllOverrides: true);
        this.UpdateSettingsForLocation(Game1.currentLocation);
    }

    /// <inheritdoc cref="IInputEvents.ButtonsChanged"/>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnButtonsChanged(object sender, ButtonsChangedEventArgs e)
    {
        if (!this.ShouldEnable(forInput: true))
            return;

        if (this.Config.Keys.FreezeTime.JustPressed())
            this.ToggleFreeze();
        else if (this.Config.Keys.IncreaseTickInterval.JustPressed())
            this.ChangeTickInterval(increase: true);
        else if (this.Config.Keys.DecreaseTickInterval.JustPressed())
            this.ChangeTickInterval(increase: false);
        else if (this.Config.Keys.ReloadConfig.JustPressed())
            this.ReloadConfig();
    }

    /// <inheritdoc cref="IPlayerEvents.Warped"/>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnWarped(object sender, WarpedEventArgs e)
    {
        if (!this.ShouldEnable() || !e.IsLocalPlayer)
            return;



        //Remark:
        //      This if statement is only here because I'm checking time intervals every tick update in multiplayer.
        //      In the future, it is better if it was only checked when anyone warps to new timezone, removes redundancy.
        //      However, then everyone would need the mod.
        if (!Game1.IsMultiplayer)
            this.UpdateSettingsForLocation(e.NewLocation);
    }

    /// <inheritdoc cref="IGameLoopEvents.TimeChanged"/>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnTimeChanged(object sender, TimeChangedEventArgs e)
    {
        if (!this.ShouldEnable())
            return;

        this.UpdateFreezeForTime();
        this.ResetTimeIntervals();
    }

    private void ResetTimeIntervals()
    {
        this.PreviousGameTimeInterval = 0;
        this.ElapsedTimeInCurrentTickInterval = 0;
        this.TargetTickProgress = 0;
    }

    /// <inheritdoc cref="IGameLoopEvents.UpdateTicked"/>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
    {
        if (!this.ShouldEnable())
            return;

        //if(Game1.IsMultiplayer)
        this.UpdateTimeIntervalSetting();
        this.TimeUpdate();
    }

    /// <summary>Simply notifies all players with this mod when time changes</summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnModMessageReceived(object sender, ModMessageReceivedEventArgs e)
    {
        if (e.FromModID == this.ModManifest.UniqueID)
            if (Enum.TryParse(e.Type, out Notifier.MessageType messageType))
            {
                if (this.Config.LocationNotify || messageType.Equals(Notifier.MessageType.Quick))
                    this.Notifier.Notify(messageType, e.ReadAs<string>());
            }
            else
                this.Monitor.Log($"Could not parse '{e.Type}' to messageType. No message was sent.", LogLevel.Warn);
    }


    /****
    ** Methods
    ****/

    private double PreviousGameTimeInterval;

    /// <summary>Runs during <see cref="ModEntry.OnUpdateTicked(object, UpdateTickedEventArgs)"/>; adds and adjusts time.</summary>
    private void TimeUpdate()
    {
        if (this.PreviousGameTimeInterval < (double)Game1.gameTimeInterval && !this.IsTimeFrozen) // If gameTimeInterval has increased since PreviousElapsedTimeInterval;
            this.ElapsedTimeInCurrentTickInterval += (Math.Abs(Game1.gameTimeInterval - this.PreviousGameTimeInterval)); // add the difference to ElapsedTimeInterval.
        else if (Game1.gameTimeInterval == 0) // If gameTimeInterval has reset to 0;
            this.ElapsedTimeInCurrentTickInterval = 0; // Change ElapsedTimeInterval to 0.

        // Calculate percentage towards target TickInterval
        this.TargetTickProgress = (double)Math.Min((double)(this.ElapsedTimeInCurrentTickInterval / this.TargetTickInterval), 1);


        // Copied from "OnTickProgressed" function, originally un-commented
        if (!this.IsTimeAdjustmentEnabledToday) // Specifically refers to festival days config
            return;

        this.TimeHelper.GameTickProgress = this.TargetTickProgress;

        // stores the current gameTimeInterval to check difference next update.
        this.PreviousGameTimeInterval = Game1.gameTimeInterval;
    }

    /// <summary>Get whether time features should be enabled.</summary>
    /// <param name="forInput">Whether to check for input handling.</param>
    private bool ShouldEnable(bool forInput = false)
    {
        // is loaded and host player (farmhands can't change time)
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return false;

        // check restrictions for input
        if (forInput)
        {
            // don't handle input when player isn't free (except in events)
            if (!Context.IsPlayerFree && !Game1.eventUp)
                return false;

            // ignore input if a textbox is active
            if (Game1.keyboardDispatcher.Subscriber is not null)
                return false;
        }

        return true;
    }

    /// <summary>Reload <see cref="Config"/> from the config file.</summary>
    private void ReloadConfig()
    {
        this.Config = this.Helper.ReadConfig<ModConfig>();
        this.UpdateTimeAdjustmentEnabledToday(Game1.season, Game1.dayOfMonth);
        this.UpdateSettingsForLocation(Game1.currentLocation);
        this.SendNotifier(Notifier.MessageType.Short, I18n.Message_ConfigReloaded());
    }

    /// <summary>Increment or decrement the tick interval, taking into account the held modifier key if applicable.</summary>
    /// <param name="increase">Whether to increment the tick interval; else decrement.</param>
    private void ChangeTickInterval(bool increase)
    {
        // Get offset to apply, starting at 1 second;
        //  Left ctrl  = 100 seconds,
        //  Left shift = 10 seconds,
        //  Left alt   = 0.1 seconds.
        int change = 1000;
        {
            KeyboardState state = Keyboard.GetState();
            if (state.IsKeyDown(Keys.LeftControl))
                change *= 100;
            else if (state.IsKeyDown(Keys.LeftShift))
                change *= 10;
            else if (state.IsKeyDown(Keys.LeftAlt))
                change /= 10;
        }

        // increase or decrease tick interval by offset
        if (increase)
            this.TargetTickInterval += change;
        // only allow decrease if TickInterval remains above the minimum allowed.
        else if (this.TargetTickInterval - change >= this.minTickIntervalAllowed)
            this.TargetTickInterval -= change;

        // log change
        this.SendNotifier(Notifier.MessageType.Quick, I18n.Message_SpeedChanged(seconds: (float)this.TargetTickInterval / 1000));
        this.Monitor.Log($"Tick length set to {this.TargetTickInterval / 1000d: 0.##} seconds.", LogLevel.Info);
    }

    /// <summary>Toggle whether time is frozen.</summary>
    private void ToggleFreeze()
    {
        if (!this.IsTimeFrozen)
        {
            this.UpdateTimeFreeze(manualOverride: true);
            this.SendNotifier(Notifier.MessageType.Quick, I18n.Message_TimeStopped());
            this.Monitor.Log("Time is frozen globally.", LogLevel.Info);
        }
        else
        {
            this.UpdateTimeFreeze(manualOverride: false);
            this.SendNotifier(Notifier.MessageType.Quick, I18n.Message_TimeResumed());
            this.Monitor.Log($"Time is resumed at \"{Game1.currentLocation.Name}\".", LogLevel.Info);
        }
    }

    /// <summary>Update the time freeze settings for the given time of day.</summary>
    private void UpdateFreezeForTime()
    {
        bool wasFrozen = this.IsTimeFrozen;
        this.UpdateTimeFreeze();

        if (!wasFrozen && this.IsTimeFrozen)
        {
            this.SendNotifier(Notifier.MessageType.Quick, I18n.Message_OnTimeChange_TimeStopped());
            this.Monitor.Log($"Time automatically set to frozen at {Game1.timeOfDay}.", LogLevel.Info);
        }
    }

    /// <summary>Update the time settings for the given location.</summary>
    /// <param name="location">The game location.</param>
    private void UpdateSettingsForLocation(GameLocation location)
    {
        if (location == null)
            return;

        // update time settings and clear location freeze-override flag
        this.UpdateTimeFreeze(clearOverride: AutoFreezeReason.FrozenForLocation);


        // Update freeze and tick interval settings
        this.UpdateTimeIntervalSetting();

        // Notifies all players with this mod
        this.NotifyIntervalChange();
    }

    /// <summary>To compare with newTickInterval</summary>
    /// <remarks>Used only in <see cref="UpdateTimeIntervalSetting"/></remarks>
    private int previousTickInterval;

    /// <summary>(Single and Multiplayer) Updates the Freeze and TickInterval settings based on all online farmers' location.</summary>
    private void UpdateTimeIntervalSetting()
    {
        // Update freeze settings
        this.UpdateTimeFreeze();

        // List of all active time intervals (based on current online farmers' locations).
        List<int> MultiLocationIntervals = new();

        // For each online farmer, update list with the farmer's current location's time interval
        foreach (Farmer farmer in Game1.getOnlineFarmers())
            MultiLocationIntervals.Add(this.Config.GetMillisecondsPerMinute(farmer.currentLocation) * 10);

        // store the new tick interval
        int newTickInterval = (int)Math.Floor(MultiLocationIntervals.Average());

        // change tick interval and notify players if tick interval has changed
        if (this.previousTickInterval != newTickInterval)
        {
            // Take the average of all time intervals.
            this.TargetTickInterval = newTickInterval;
            // Notify all players of the time change.
            this.NotifyIntervalChange();
        }
        // Store previous tick interval
        this.previousTickInterval = newTickInterval;
    }

    /// <summary>Sends notification to each player regarding time status</summary>
    private void NotifyIntervalChange()
    {
        // Logs time interval
        this.Monitor.Log($"TimeInterval: {this.TargetTickInterval}");

        // notify player
        if (this.Config.LocationNotify)
        {
            switch (this.AutoFreeze)
            {
                case AutoFreezeReason.FrozenAtTime when this.IsTimeFrozen:
                    this.SendNotifier(Notifier.MessageType.Short, I18n.Message_OnLocationChange_TimeStoppedGlobally());
                    break;

                case AutoFreezeReason.FrozenForLocation when this.IsTimeFrozen:
                    this.SendNotifier(Notifier.MessageType.Short, I18n.Message_OnLocationChange_TimeStoppedHere());
                    break;

                case AutoFreezeReason.FrozenBeforePassOut when this.IsTimeFrozen:
                    this.SendNotifier(Notifier.MessageType.Short, I18n.Message_OnLocationChange_TimeStoppedGloballyPassOut());
                    break;

                default:
                    this.SendNotifier(Notifier.MessageType.Short, I18n.Message_OnLocationChange_TimeSpeedHere(seconds: this.TargetTickInterval / 1000));
                    break;
            }
        }
    }

    /// <summary>Sends the <see cref="Notifier"/> message to all players with mod.</summary>
    /// <param name="messageType"><see cref="Notifier.MessageType.Quick"/> (1sec) or <see cref="Notifier.MessageType.Short"/> (2sec).</param>
    /// <param name="message">Message to send in-game.</param>
    private void SendNotifier(Notifier.MessageType messageType, string message)
    {
        this.Helper.Multiplayer.SendMessage(message, messageType.ToString());
        this.Notifier.Notify(messageType, message);
    }

    /// <summary>Update the <see cref="AutoFreeze"/> and <see cref="ManualFreeze"/> flags based on the current context.</summary>
    /// <param name="manualOverride">An explicit freeze (<c>true</c>) or unfreeze (<c>false</c>) requested by the player, if applicable.</param>
    /// <param name="clearPreviousOverrides">Whether to clear any previous explicit overrides.</param>
    private void UpdateTimeFreeze(bool? manualOverride = null, AutoFreezeReason clearOverride = AutoFreezeReason.None, bool clearAllOverrides = false)
    {
        bool? wasManualFreeze = this.ManualFreeze;
        AutoFreezeReason wasAutoFreeze = this.AutoFreeze;

        // update auto freeze
        this.AutoFreeze = this.GetAutoFreezeType();

        // update manual freeze
        if (manualOverride.HasValue)
            this.ManualFreeze = manualOverride.Value;

        // Clear marked override flags
        switch (clearOverride)
        {
            case AutoFreezeReason.FrozenForLocation:
                this.FreezeOverrideLocation = false;
                break;
            case AutoFreezeReason.FrozenAtTime:
                this.FreezeOverrideTime = false;
                break;
            case AutoFreezeReason.FrozenBeforePassOut:
                this.FreezeOverridePassOut = false;
                break;
            default:
                break;
        }

        // Flag AutoFreeze overrides if manually unfrozen
        if (manualOverride == false)
        {
            switch (this.AutoFreeze)
            {
                case AutoFreezeReason.FrozenForLocation:
                    this.FreezeOverrideLocation = true;
                    break;
                case AutoFreezeReason.FrozenAtTime:
                    this.FreezeOverrideTime = true;
                    break;
                case AutoFreezeReason.FrozenBeforePassOut:
                    this.FreezeOverridePassOut = true;
                    break;
            }
        }

        // clear Freeze overrides if no longer needed or marked for clearing
        if (this.ManualFreeze == false && this.AutoFreeze == AutoFreezeReason.None
            || clearAllOverrides)
        {
            this.FreezeOverrideLocation = false;
            this.FreezeOverridePassOut = false;
            this.FreezeOverrideTime = false;
        }

        // log change
        if (wasAutoFreeze != this.AutoFreeze)
            this.Monitor.Log($"Auto freeze changed from {wasAutoFreeze} to {this.AutoFreeze}.");
        if (wasManualFreeze != this.ManualFreeze)
            this.Monitor.Log($"Manual freeze changed from {wasManualFreeze?.ToString() ?? "null"} to {this.ManualFreeze}.");
    }

    /// <summary>Update the time adjustment settings for the given date.</summary>
    /// <param name="season">The current season.</param>
    /// <param name="dayOfMonth">The current day of month.</param>
    private void UpdateTimeAdjustmentEnabledToday(Season season, int dayOfMonth)
    {
        this.IsTimeAdjustmentEnabledToday = this.Config.ShouldTimeAdjustOnDay(season, dayOfMonth);
    }

    /// <summary>Get the freeze type which applies for the current context, ignoring overrides by the player.</summary>
    private AutoFreezeReason GetAutoFreezeType()
    {
        if (this.Config.ShouldFreeze(Game1.currentLocation))
            return AutoFreezeReason.FrozenForLocation;

        if (this.Config.ShouldFreeze(Game1.timeOfDay, passOutCheck: true))
            return AutoFreezeReason.FrozenBeforePassOut;

        if (this.Config.ShouldFreeze(Game1.timeOfDay))
            return AutoFreezeReason.FrozenAtTime;

        return AutoFreezeReason.None;
    }
}
