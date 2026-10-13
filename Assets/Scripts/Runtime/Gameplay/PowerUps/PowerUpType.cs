namespace Runtime.Gameplay.PowerUps
{
    public enum PowerUpType
    {
        Scissors,
        TimeFreeze,
        Shuffle
    }

    // Requested: waiting for a target or a confirm. Started: count spent, animation running.
    // Performed: the effect is applied to the board.
    public enum PowerUpState
    {
        Idle,
        Requested,
        Started,
        Performed
    }
}
