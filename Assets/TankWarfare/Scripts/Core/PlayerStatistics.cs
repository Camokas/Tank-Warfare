using System;

namespace TankWarfare.Core
{
    [Serializable]
    public sealed class PlayerStatistics
    {
        public int schemaVersion = 1;
        public string nickname = "Игрок";
        public int victories;
        public int defeats;
        public int matches;
        public double metersDriven;
        public int shotsFired;
        public int wallsBroken;
        public int heavyPicks;
        public int mediumPicks;
        public int lightPicks;

        public TankType FavoriteTank
        {
            get
            {
                if (heavyPicks >= mediumPicks && heavyPicks >= lightPicks)
                    return TankType.Heavy;
                if (lightPicks >= mediumPicks)
                    return TankType.Light;
                return TankType.Medium;
            }
        }

        public void AddMatch(bool won, TankType tankType, MatchPlayerStatistics match)
        {
            matches++;
            if (won) victories++;
            else defeats++;

            metersDriven += Math.Max(0d, match?.meters ?? 0d);
            shotsFired += Math.Max(0, match?.shots ?? 0);
            wallsBroken += Math.Max(0, match?.walls ?? 0);

            switch (tankType)
            {
                case TankType.Heavy: heavyPicks++; break;
                case TankType.Light: lightPicks++; break;
                default: mediumPicks++; break;
            }
        }
    }

    [Serializable]
    public sealed class MatchPlayerStatistics
    {
        public int playerId;
        public int shots;
        public float meters;
        public int walls;
    }
}
