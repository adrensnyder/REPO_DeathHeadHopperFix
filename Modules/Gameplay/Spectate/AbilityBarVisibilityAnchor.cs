#nullable enable

using System.Collections.Generic;

namespace DeathHeadHopperFix.Modules.Gameplay.Spectate
{
    public static class AbilityBarVisibilityAnchor
    {
        private static readonly HashSet<string> ActiveDemands = new();

        public static bool HasExternalDemand()
        {
            lock (ActiveDemands)
            {
                return ActiveDemands.Count > 0;
            }
        }

        public static void SetExternalDemand(string sourceId, bool active)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                return;
            }

            lock (ActiveDemands)
            {
                if (active)
                {
                    ActiveDemands.Add(sourceId);
                }
                else
                {
                    ActiveDemands.Remove(sourceId);
                }
            }
        }

        public static void ClearExternalDemands()
        {
            lock (ActiveDemands)
            {
                ActiveDemands.Clear();
            }
        }
    }
}
