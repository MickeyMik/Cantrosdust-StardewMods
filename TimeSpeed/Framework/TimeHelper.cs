using System;
using StardewValley;

namespace TimeSpeed.Framework
{
    /// <summary>Provides helper methods for tracking time flow.</summary>
    internal class TimeHelper
    {
        /*********
        ** Accessors
        *********/
        /// <summary>The game's default tick interval in milliseconds for the current location.</summary>
        /// <remarks>10 minutes in-game is normally 7000 ms, but skull cavern, for example, adds 2000 ms per 10 minutes;
        ///             It would be possible to use Game1.realMilliSecondsPerGameTenMinutes, I am unsure why it isn't used;
        ///             For the same reason, I wonder if the ExtraMillisecondsPerInGameMinute needs to be multiplied by ten?</remarks>
        public int CurrentDefaultTickInterval => 7000 + (Game1.currentLocation?.ExtraMillisecondsPerInGameMinute ?? 0);

        /// <summary>The percentage of the <see cref="CurrentDefaultTickInterval"/> that's elapsed since the last tick.</summary>
        public double TickProgress
        {
            get => (double)Game1.gameTimeInterval / this.CurrentDefaultTickInterval;
            // the math.floor stops rounding errors causing time-skip, e.g. 6999.5 milliseconds rounding to 7000 and then progressing time.
            set => Game1.gameTimeInterval = (int)(Math.Floor(value * this.CurrentDefaultTickInterval));
        }
    }
}
