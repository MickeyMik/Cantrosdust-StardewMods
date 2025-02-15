using StardewValley;

namespace TimeSpeed.Framework;

/// <summary>Displays messages to the user in-game.</summary>
internal class Notifier
{
    /*********
        ** Properties
        *********/
    /// <summary>Whether message should be 'Quick' (displayed for one second) or 'Short' (display for two seconds).</summary>
    public enum MessageType { Quick = 1000, Short = 2000 }

    /*********
** Public methods
*********/
    /// <summary>Display an in-game message.</summary>
    /// <param name="message">The message to display.</param>
    public void Notify(MessageType type, string message) => Game1.addHUDMessage(new HUDMessage(message, HUDMessage.newQuest_type) { timeLeft = (int)type });
}
