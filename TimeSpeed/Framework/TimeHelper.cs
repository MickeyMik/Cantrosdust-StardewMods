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
        public int CurrentDefaultTickInterval => 7000 + (Game1.currentLocation?.ExtraMillisecondsPerInGameMinute ?? 0);

        /// <summary>The percentage of the <see cref="CurrentDefaultTickInterval"/> that's elapsed since the last tick.</summary>
        public double TickProgress
        {
            get => (double)Game1.gameTimeInterval / this.CurrentDefaultTickInterval;
            set => Game1.gameTimeInterval = (int)(Math.Floor(value * this.CurrentDefaultTickInterval));
        }
    }
}
