using System;
using System.Reflection;
using ColossalFramework.Globalization;

namespace UndergroundParkingGarage
{
    internal static class UndergroundParkingLocalization
    {
        public const string BuildingTitle = "Underground Parking Entrance";
        public const string BuildingDescription = "Provides access to an underground car park.";

        private static bool _loggedSuccess;
        private static Locale _registeredLocale;

        public static void Apply()
        {
            try
            {
                LocaleManager manager = LocaleManager.instance;
                if (manager == null)
                    return;

                FieldInfo localeField = typeof(LocaleManager).GetField(
                    "m_Locale",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Locale locale = localeField == null ? null : localeField.GetValue(manager) as Locale;
                if (locale == null)
                    return;

                if (ReferenceEquals(locale, _registeredLocale))
                    return;

                for (int index = 0;
                     index < UndergroundParkingStandaloneCatalog.VariantCount;
                     index++)
                {
                    UndergroundParkingStandaloneSpec spec =
                        UndergroundParkingStandaloneCatalog.Get(
                            (UndergroundParkingStandaloneVariant)index);
                    AddOverrideAndVerify(
                        locale, "BUILDING_TITLE", spec.PrefabName, spec.Title);
                    AddOverrideAndVerify(
                        locale, "BUILDING_DESC", spec.PrefabName, spec.Description);
                    AddOverrideAndVerify(
                        locale, "BUILDING_SHORT_DESC", spec.PrefabName, spec.Description);
                }
                _registeredLocale = locale;

                if (!_loggedSuccess)
                {
                    _loggedSuccess = true;
                    UndergroundParkingLog.Advanced("Building UI text registered: title="
                                                + BuildingTitle
                                                + " key="
                                                + UndergroundParkingBuildingPrefab.PrefabName);
                }
            }
            catch (Exception e)
            {
                UndergroundParkingLog.Warning("Building UI text registration failed: " + e.Message);
            }
        }

        private static void AddOverrideAndVerify(
            Locale locale,
            string identifier,
            string key,
            string value)
        {
            try
            {
                locale.AddLocalizedString(
                    new Locale.Key
                    {
                        m_Identifier = identifier,
                        m_Key = key
                    },
                    value);
            }
            catch (ArgumentException)
            {
                // A locale entry can survive a level reload. The exact override
                // below intentionally replaces its player-facing value.
            }

            Locale.SetOverriddenLocalizedStrings(identifier, key, new[] { value });
            string resolved = Locale.GetUnchecked(identifier, key);
            if (!string.Equals(resolved, value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Kiosk locale verification failed: identifier="
                    + identifier
                    + " key="
                    + key
                    + " resolved="
                    + (resolved ?? "null"));
            }
        }
    }
}
