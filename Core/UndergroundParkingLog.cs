using Debug = UnityEngine.Debug;

namespace UndergroundParkingGarage
{
    internal static class UndergroundParkingLog
    {
        private const string Prefix = "[UndergroundParkingGarage] ";

        public static void Info(string message)
        {
            Debug.Log(Prefix + message);
        }

        public static void Advanced(string message)
        {
            if (UndergroundParkingGarageSettings.AdvancedDiagnostics)
                Debug.Log(Prefix + message);
        }

        public static void AdvancedWarning(string message)
        {
            if (UndergroundParkingGarageSettings.AdvancedDiagnostics)
                Debug.LogWarning(Prefix + message);
        }

        public static void Warning(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        public static void Error(string message)
        {
            Debug.LogError(Prefix + message);
        }
    }
}
