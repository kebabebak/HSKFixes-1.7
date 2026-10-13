using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace HSKFixes17
{
    /// <summary>
    /// Замена сломанного префикса Skynet_SK — мода из состава пака.
    ///
    /// В его статическом конструкторе подряд идут два патча, и второй скопирован с первого вместе
    /// с типом __instance:
    ///
    ///     val.Patch(AccessTools.Method(typeof(JobGiver_EatInGatheringArea), "TryGiveJob"),
    ///               new HarmonyMethod(... "CompatPatch_EatInPartyAreaTryGiveJob"));   // верно
    ///     val.Patch(AccessTools.Method(typeof(JobGiver_GetJoy), "TryGiveJob"),
    ///               new HarmonyMethod(... "CompatPatch_GetJoyTryGiveJob"));           // __instance
    ///                                                                                 // остался
    ///                                                                                 // от первого
    ///
    ///     public static bool CompatPatch_GetJoyTryGiveJob(
    ///         ref JobGiver_EatInGatheringArea __instance, ref Job __result, ref Pawn pawn)
    ///
    /// Harmony на 1.7 такую привязку отвергает, и цена здесь выше, чем у STL с Minerals. Падает не
    /// хвост списка, а сам конструктор — тип остаётся неинициализированным. При этом постфикс
    /// Patch_Pawn_NeedsTracker_ShouldHaveNeed к тому моменту уже поставлен, и каждое обращение к
    /// нему бросает TypeInitializationException. Вызывают его на каждую пешку и каждую нужду:
    ///
    ///     Error while determining if <пешка> should have Need Chemical_Alcohol:
    ///     TypeInitializationException ... ---> ArgumentException: Unrelated classes cannot
    ///     describe the same non-null object ... RimWorld.JobGiver_GetJoy::TryGiveJob
    ///
    /// В замере это 37 строк на пешку — несколько тысяч за загрузку сейва.
    ///
    /// Логика оригинала сохранена целиком: если у дефа пешки есть SkynetPawnExtension, оригинал
    /// пропускается и работа не выдаётся. __instance в их теле не используется вовсе, поэтому
    /// замена просто не объявляет его. Тип расширения ищется по имени: жёсткой ссылки на Skynet
    /// у нас нет и быть не должно.
    /// </summary>
    public static class SkynetJoyFix
    {
        private const string PatchesTypeName = "Skynet.HarmonyPatches";
        private const string BrokenPrefixName = "CompatPatch_GetJoyTryGiveJob";
        private const string ExtensionTypeName = "Skynet.SkynetPawnExtension";

        private static Type extension;

        /// <summary>Опознаёт сломанный патч; сам факт поломки проверяет BrokenPatchFix.</summary>
        public static MethodInfo ReplacementFor(MethodInfo patch)
        {
            if (patch.Name != BrokenPrefixName)
                return null;
            if (patch.DeclaringType == null || patch.DeclaringType.FullName != PatchesTypeName)
                return null;

            if (extension == null)
            {
                extension = AccessTools.TypeByName(ExtensionTypeName);
                if (extension == null)
                {
                    Log.Warning("[HSK Fixes 1.7] не найден тип " + ExtensionTypeName
                        + " — префикс Skynet оставлен как есть.");
                    return null;
                }
            }

            return AccessTools.Method(typeof(SkynetJoyFix), nameof(NoJoyForSkynetPawns));
        }

        /// <summary>Та же логика, правильная подпись: без __instance, которого там и не было.</summary>
        public static bool NoJoyForSkynetPawns(ref Job __result, Pawn pawn)
        {
            if (!HasSkynetExtension(pawn))
                return true;

            __result = null;
            return false;
        }

        /// <summary>
        /// Def.HasModExtension есть только обобщённый, а тип у нас в переменной — поэтому идём
        /// по списку расширений сами.
        /// </summary>
        private static bool HasSkynetExtension(Pawn pawn)
        {
            List<DefModExtension> extensions = pawn?.def?.modExtensions;
            if (extensions == null)
                return false;

            for (int i = 0; i < extensions.Count; i++)
            {
                if (extension.IsInstanceOfType(extensions[i]))
                    return true;
            }
            return false;
        }
    }
}
