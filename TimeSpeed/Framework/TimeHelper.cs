using System;
using StardewValley;

namespace TimeSpeed.Framework;

/// <summary>Provides helper methods for tracking time flow.</summary>
internal class TimeHelper
{
    /*********
    ** Accessors
    *********/
    /// <summary>The game's default 10-minute clock-tick interval in milliseconds for the current location.</summary>
    /// <remarks>10 minutes in-game is normally 7000 ms, but skull cavern, for example, adds 2000 ms per 10 minutes</remarks>
    public int CurrentDefaultTickInterval => Game1.realMilliSecondsPerGameTenMinutes + (Game1.currentLocation?.ExtraMillisecondsPerInGameMinute * 10 ?? 0);

    /// <summary>The percentage of the <see cref="CurrentDefaultTickInterval"/> that's elapsed since the last tick.</summary>
    public double GameTickProgress
    {
        get => (double)Game1.gameTimeInterval / this.CurrentDefaultTickInterval;
        // the math.floor stops rounding errors causing time-skip
        // example: 6999.5 milliseconds rounding to 7000 and then progressing time.
        set => Game1.gameTimeInterval = (int)(Math.Floor(value * this.CurrentDefaultTickInterval));
    }
}
