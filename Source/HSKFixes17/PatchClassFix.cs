using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace HSKFixes17
{
    /// <summary>
    /// То же, что BrokenPatchFix, но для патчей, объявленных атрибутами и поставленных через
    /// PatchAll.
    ///
    /// Почему нужен второй перехват. BrokenPatchFix висит на Harmony.Patch, а PatchAll туда не
    /// заходит: он идёт по типам сборки и зовёт CreateClassProcessor(type).Patch(). Так патчат
    /// Rimatomics и Dubs Mint Menus, и у каждого нашёлся класс с тем же изъяном — __instance
    /// объявлен типом, которого патчимый метод не предоставляет:
    ///
    ///     Rimatomics  Harmony_Site_ShouldRemoveMapNow.Postfix(DestroyedSettlement __instance, ...)
    ///                 на Site.ShouldRemoveMapNow — тип скопирован из соседнего класса
    ///                 Harmony_DestroyedSettlement_ShouldRemoveMapNow, где он верен. Падает весь
    ///                 статический конструктор мода, то есть все его патчи.
    ///     DubsMintMenus  Patch_InterfaceTryActivate.Prefix(MainButtonWorker __instance)
    ///                 на MainTabsRoot.HandleLowPriorityShortcuts. Тоже уносит конструктор.
    ///
    /// Как работает. Префикс на PatchClassProcessor.Patch читает приватное поле containerType —
    /// класс, который сейчас обрабатывается. Если это один из известных, мы ставим исправленный
    /// патч сами, их же Harmony-экземпляром, и возвращаем false: их регистрация не выполняется и
    /// падать нечему. Остальные классы идут обычным путём.
    ///
    /// Замены повторяют логику оригиналов. У Dubs __instance не используется вовсе, поэтому
    /// вызывается их же метод с null на этом месте — логика остаётся их целиком. У Rimatomics
    /// __instance нужен (из него берут Map), но в сигнатуре достаточно MapParent, от которого Site
    /// и происходит, так что тело воспроизведено: если на карте есть удар ICBM, карту не удалять.
    /// </summary>
    public static class PatchClassFix
    {
        private const string HarmonyId = "linya.hskfixes17";

        private static FieldInfo containerTypeField;
        private static FieldInfo harmonyField;
        private static MethodInfo dubsPrefix;
        private static FieldInfo icbmStrike;

        public static void Install()
        {
            MethodInfo patch = AccessTools.Method(typeof(PatchClassProcessor), nameof(PatchClassProcessor.Patch));
            containerTypeField = AccessTools.Field(typeof(PatchClassProcessor), "containerType");
            harmonyField = AccessTools.Field(typeof(PatchClassProcessor), "instance");

            if (patch == null || containerTypeField == null || harmonyField == null)
            {
                Log.Warning("[HSK Fixes 1.7] не найдены внутренности PatchClassProcessor —"
                    + " подмены атрибутных патчей не будет.");
                return;
            }

            new Harmony(HarmonyId).Patch(patch,
                new HarmonyMethod(typeof(PatchClassFix), nameof(ReplaceKnownBrokenClass)));
        }

        /// <summary>
        /// Выполняется на каждом патч-классе в игре, поэтому сперва самая дешёвая проверка —
        /// имя типа.
        /// </summary>
        private static bool ReplaceKnownBrokenClass(PatchClassProcessor __instance,
            ref List<MethodInfo> __result)
        {
            string name;
            Harmony harmony;
            try
            {
                name = (containerTypeField.GetValue(__instance) as Type)?.FullName;
                harmony = harmonyField.GetValue(__instance) as Harmony;
            }
            catch (Exception)
            {
                return true;
            }

            if (name == null || harmony == null)
                return true;

            bool handled;
            switch (name)
            {
                case "Rimatomics.HarmonyPatches+Harmony_Site_ShouldRemoveMapNow":
                    handled = FixRimatomicsSite(harmony);
                    break;
                case "DubsMintMenus.Patch_InterfaceTryActivate":
                    handled = FixMintMenusShortcut(harmony);
                    break;
                default:
                    return true;
            }

            if (!handled)
                return true;

            __result = new List<MethodInfo>();
            return false;
        }

        private static bool FixRimatomicsSite(Harmony harmony)
        {
            if (icbmStrike == null)
            {
                icbmStrike = AccessTools.Field(AccessTools.TypeByName("Rimatomics.DubDef"), "ICBMStrike");
                if (icbmStrike == null)
                {
                    Log.Warning("[HSK Fixes 1.7] не найдено Rimatomics.DubDef.ICBMStrike —"
                        + " класс Harmony_Site_ShouldRemoveMapNow оставлен как есть.");
                    return false;
                }
            }

            MethodInfo target = AccessTools.Method(typeof(Site), nameof(Site.ShouldRemoveMapNow));
            if (target == null)
                return false;

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(PatchClassFix), nameof(KeepMapWithIcbmStrike)));
            Report("Rimatomics.Harmony_Site_ShouldRemoveMapNow", "Site.ShouldRemoveMapNow");
            return true;
        }

        /// <summary>Их логика: пока на карте есть удар ICBM, карту удалять нельзя.</summary>
        public static void KeepMapWithIcbmStrike(MapParent __instance, ref bool __result)
        {
            if (!__result)
                return;

            ThingDef strike = icbmStrike.GetValue(null) as ThingDef;
            Map map = __instance?.Map;
            if (strike == null || map == null)
                return;

            if (map.listerThings.ThingsOfDef(strike).Count > 0)
                __result = false;
        }

        private static bool FixMintMenusShortcut(Harmony harmony)
        {
            if (dubsPrefix == null)
            {
                dubsPrefix = AccessTools.Method("DubsMintMenus.Patch_InterfaceTryActivate:Prefix");
                if (dubsPrefix == null)
                {
                    Log.Warning("[HSK Fixes 1.7] не найден DubsMintMenus.Patch_InterfaceTryActivate.Prefix —"
                        + " класс оставлен как есть.");
                    return false;
                }
            }

            MethodInfo target = AccessTools.Method(typeof(MainTabsRoot), "HandleLowPriorityShortcuts");
            if (target == null)
                return false;

            harmony.Patch(target, new HarmonyMethod(typeof(PatchClassFix), nameof(MintWheelShortcut)));
            Report("DubsMintMenus.Patch_InterfaceTryActivate", "MainTabsRoot.HandleLowPriorityShortcuts");
            return true;
        }

        /// <summary>
        /// Их же метод, вызванный с null вместо неиспользуемого __instance: логика остаётся
        /// полностью их, включая чтение их настроек.
        /// </summary>
        public static bool MintWheelShortcut()
        {
            try
            {
                return (bool)dubsPrefix.Invoke(null, new object[] { null });
            }
            catch (Exception e)
            {
                Log.WarningOnce("[HSK Fixes 1.7] префикс Dubs Mint Menus завершился ошибкой: "
                    + (e.InnerException ?? e).Message, 0x4D4D4D01);
                return true;
            }
        }

        private static void Report(string patchClass, string target)
        {
            Log.Message("[HSK Fixes 1.7] класс патчей " + patchClass + " заменён своим на " + target
                + ": в оригинале __instance объявлен несовместимым типом, и Harmony 1.7 отвергала"
                + " его вместе со всем статическим конструктором мода. Логика сохранена.");
        }
    }
}
