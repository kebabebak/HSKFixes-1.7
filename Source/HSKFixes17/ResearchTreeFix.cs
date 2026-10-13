using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace HSKFixes17
{
    /// <summary>
    /// Дерево исследований на 1.7: при прокрутке окно «стирается» — на месте уехавших узлов
    /// остаётся пустота.
    ///
    /// ПРИЧИНА. Мод прокручивает дерево одним вызовом, скопированным из внутренностей Unity:
    ///
    ///     public static void BeginScrollView(Rect position, Vector2 scrollPosition, Rect viewRect)
    ///     {
    ///         PushAction(position,
    ///                    new Vector2(-scrollPosition.x - viewRect.x, -scrollPosition.y - viewRect.y),
    ///                    Vector2.zero, arg4: false);
    ///     }
    ///
    /// где PushAction — это UnityEngine.GUIClip.Push, добытый рефлексией. А заглушка 1.7 второй
    /// аргумент выбрасывает:
    ///
    ///     internal static void Internal_Push(Rect screenRect, Vector2 scrollOffset,
    ///                                        Vector2 renderOffset, bool resetOffset)
    ///     {
    ///         GuiBackend.Current?.PushGroup(screenRect);     // смещение не передано
    ///     }
    ///
    /// Прямоугольник обрезания ставится верно, содержимое не сдвигается. Дерево всегда рисуется в
    /// позиции прокрутки «ноль»: мод считает, что уехал, и рисует узлы нового участка, а пиксели
    /// остаются на месте — там, где узлов по его расчётам больше нет, остаётся фон.
    ///
    /// ЧТО ДЕЛАЕМ. Нужный примитив у движка есть, и ванильный скролл им и пользуется:
    ///
    ///     Vector2 contentOffset = new Vector2(position.x - scrollPosition.x - viewRect.x,
    ///                                         position.y - scrollPosition.y - viewRect.y);
    ///     GuiBackend.Current?.PushScrollView(clipRect, contentOffset);
    ///
    /// Формула совпадает с намерением мода, поэтому его BeginScrollView/EndScrollView
    /// перенаправлены на публичные GUI.BeginScrollView/GUI.EndScrollView. Полосы прокрутки
    /// запрошены стилем none — мод рисует свои сам, а handleScrollWheel выключен, чтобы ванильный
    /// скролл не перехватывал колесо.
    ///
    /// ПРО ЗУМ — отдельная мера, и она не подтверждена. Колесо в этом моде не прокручивает, а
    /// меняет масштаб, а масштабирование сделано подменой глобального состояния:
    ///
    ///     GUI.matrix = Matrix4x4.TRS(0, identity, new Vector3(UIScale / ZoomLevel, ...));
    ///     UI.screenWidth  = Mathf.RoundToInt(Screen.width  / UIScale * ZoomLevel);
    ///
    /// Сперва я решил, что чернота от этого, и закрепил масштаб на 100%. Это не помогло: с
    /// закреплённым зумом ползунки стирали картинку по-прежнему, то есть причина была другая —
    /// та, что выше. Закрепление оставлено, потому что следует ли новый бэкенд за GUI.matrix,
    /// по-прежнему неизвестно, а подмена UI.screenWidth глобальна. Когда прокрутка подтвердится,
    /// это стоит снять и проверить зум отдельно — убрать PinZoom из Install, одна строка.
    ///
    /// Чинить по-настоящему должен автор: в About.xml у мода заявлена только 1.6.
    /// </summary>
    public static class ResearchTreeFix
    {
        private const string HarmonyId = "linya.hskfixes17";
        private const string TreeTypeName = "ResearchTreeSK.Tree";
        private const string ClipUtilityTypeName = "ResearchTreeSK.GUIClipUtility";

        public static void Install()
        {
            Harmony harmony = new Harmony(HarmonyId);
            bool scroll = FixScrolling(harmony);
            bool zoom = PinZoom(harmony);

            if (scroll)
            {
                Log.Message("[HSK Fixes 1.7] прокрутка дерева исследований исправлена: мод сдвигает"
                    + " содержимое через GUIClip.Push, а заглушка 1.7 смещение выбрасывает, и дерево"
                    + " рисовалось всегда в позиции ноль. Переведено на GUI.BeginScrollView, который"
                    + " пользуется PushScrollView движка."
                    + (zoom ? " Масштаб закреплён на 100% — мера временная." : string.Empty));
            }
        }

        /// <summary>Настоящая правка: вернуть содержимому смещение прокрутки.</summary>
        private static bool FixScrolling(Harmony harmony)
        {
            Type clip = AccessTools.TypeByName(ClipUtilityTypeName);
            if (clip == null)
                return false; // мод не установлен

            MethodInfo begin = AccessTools.Method(clip, "BeginScrollView");
            MethodInfo end = AccessTools.Method(clip, "EndScrollView");
            if (begin == null || end == null)
            {
                Log.Warning("[HSK Fixes 1.7] у ResearchTreeSK.GUIClipUtility не найдены"
                    + " BeginScrollView/EndScrollView — прокрутка дерева оставлена как есть.");
                return false;
            }

            harmony.Patch(begin, new HarmonyMethod(typeof(ResearchTreeFix), nameof(BeginScrollView)));
            harmony.Patch(end, new HarmonyMethod(typeof(ResearchTreeFix), nameof(EndScrollView)));
            return true;
        }

        /// <summary>
        /// Имена параметров совпадают с оригиналом — Harmony связывает их по именам.
        /// </summary>
        public static bool BeginScrollView(Rect position, Vector2 scrollPosition, Rect viewRect)
        {
            GUI.BeginScrollView(position, scrollPosition, viewRect, GUIStyle.none, GUIStyle.none);
            return false;
        }

        public static bool EndScrollView()
        {
            GUI.EndScrollView(false);
            return false;
        }

        /// <summary>
        /// Временная мера: не давать масштабу уходить от 100%, пока неизвестно, следует ли новый
        /// бэкенд за GUI.matrix. Путей два — колесо и кнопки, — но оба присваивают свойство, так что
        /// одного префикса на сеттер хватило бы; HandleZoom перехвачен ещё и затем, чтобы он не
        /// съедал событие колеса (Event.current.Use) и прокрутка доставалась скроллу.
        /// </summary>
        private static bool PinZoom(Harmony harmony)
        {
            Type tree = AccessTools.TypeByName(TreeTypeName);
            if (tree == null)
                return false;

            MethodInfo handleZoom = AccessTools.Method(tree, "HandleZoom");
            MethodInfo setZoom = AccessTools.PropertySetter(tree, "ZoomLevel");
            if (handleZoom == null || setZoom == null)
            {
                Log.Warning("[HSK Fixes 1.7] у ResearchTreeSK.Tree не найдены HandleZoom или сеттер"
                    + " ZoomLevel — масштаб дерева оставлен как есть.");
                return false;
            }

            harmony.Patch(handleZoom, new HarmonyMethod(typeof(ResearchTreeFix), nameof(SkipWheelZoom)));
            harmony.Patch(setZoom, new HarmonyMethod(typeof(ResearchTreeFix), nameof(KeepZoomAtOne)));
            return true;
        }

        /// <summary>Колесо не зумит и, главное, не съедается.</summary>
        public static bool SkipWheelZoom()
        {
            return false;
        }

        /// <summary>Любое присваивание масштаба приводится к 100%.</summary>
        public static bool KeepZoomAtOne(ref float value)
        {
            value = 1f;
            return true;
        }
    }
}
