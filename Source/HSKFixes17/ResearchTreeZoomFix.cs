using System.Reflection;
using HarmonyLib;
using Verse;

namespace HSKFixes17
{
    /// <summary>
    /// Дерево исследований на 1.7: при вращении колеса окно закрашивается чёрным.
    ///
    /// Что происходит. Колесо в этом моде не прокручивает, а меняет масштаб:
    ///
    ///     private static void HandleZoom(Rect visibleRect)
    ///     {
    ///         if (visibleRect.Contains(Event.current.mousePosition) &amp;&amp; Event.current.isScrollWheel)
    ///         {
    ///             ZoomLevel = Mathf.Clamp(..., 1f, MaxZoomLevel);
    ///             ScrollPosition = mousePosition - val * ZoomLevel;
    ///             Event.current.Use();        // событие съедено, до прокрутки дело не доходит
    ///         }
    ///     }
    ///
    /// А масштабирование сделано подменой глобального состояния GUI:
    ///
    ///     GUI.matrix = Matrix4x4.TRS(0, identity, new Vector3(UIScale / ZoomLevel, UIScale / ZoomLevel, 1));
    ///     UI.screenWidth  = Mathf.RoundToInt(Screen.width  / UIScale * ZoomLevel);
    ///     UI.screenHeight = Mathf.RoundToInt(Screen.height / UIScale * ZoomLevel);
    ///
    /// Пока ZoomLevel равен 1 (и UI Scale равен 1), это тождественное преобразование, и дерево
    /// рисуется нормально — поломка начинается с первого щелчка колеса. Новый движок обрезает
    /// отрисовку по своим прямоугольникам и матрицу GUI при этом не учитывает: содержимое уезжает
    /// за них, и на его месте остаётся фон окна.
    ///
    /// Рефлексия мода тут ни при чём, я проверял: UnityStubs.dll объявляет UnityEngine.GUIClip,
    /// в нём сохранён даже опорный прямоугольник (-10000, -10000, 40000, 40000), с которым мод
    /// сравнивает GetTopRect(), и GetTopRect/Push/Pop на месте с нужными сигнатурами. Ломается
    /// именно трюк с матрицей.
    ///
    /// Чинить по-настоящему должен автор мода: в его About.xml заявлена только 1.6, то есть на 1.7
    /// он не портирован, и масштабирование надо переписывать под новый бэкенд.
    ///
    /// Что делает эта правка. Запрещает менять масштаб — тогда ZoomLevel всегда 1, матрица
    /// единичная, и дерево рисуется. Два места, потому что путей два:
    ///
    ///     HandleZoom     колесо. Префикс возвращает false, то есть Event.current.Use() не
    ///                    выполняется и событие достаётся внешнему скроллу — колесо, скорее всего,
    ///                    начнёт именно прокручивать, чего от него и ждут.
    ///     set_ZoomLevel  кнопки «приблизить/отдалить» и подгонка по экрану. Все они присваивают
    ///                    свойство, поэтому достаточно одного префикса на сеттер.
    ///
    /// Цена: отдалиться и увидеть дерево целиком больше нельзя. В дереве HSK это заметно, но
    /// рабочее окно на 100% лучше чёрного. Выключается удалением одной строки в HSKFixes17Mod.
    /// </summary>
    public static class ResearchTreeZoomFix
    {
        private const string HarmonyId = "linya.hskfixes17";
        private const string TreeTypeName = "ResearchTreeSK.Tree";

        public static void Install()
        {
            System.Type tree = AccessTools.TypeByName(TreeTypeName);
            if (tree == null)
                return; // мод не установлен — нечего и чинить

            MethodInfo handleZoom = AccessTools.Method(tree, "HandleZoom");
            MethodInfo setZoom = AccessTools.PropertySetter(tree, "ZoomLevel");

            if (handleZoom == null || setZoom == null)
            {
                Log.Warning("[HSK Fixes 1.7] у ResearchTreeSK.Tree не найдены HandleZoom или сеттер"
                    + " ZoomLevel — масштаб дерева исследований оставлен как есть.");
                return;
            }

            var harmony = new Harmony(HarmonyId);
            harmony.Patch(handleZoom, new HarmonyMethod(typeof(ResearchTreeZoomFix), nameof(SkipWheelZoom)));
            harmony.Patch(setZoom, new HarmonyMethod(typeof(ResearchTreeZoomFix), nameof(KeepZoomAtOne)));

            Log.Message("[HSK Fixes 1.7] масштаб дерева исследований закреплён на 100%: на 1.7 зум"
                + " подменяет GUI.matrix и UI.screenWidth, а новый движок этого не учитывает — окно"
                + " закрашивалось чёрным. Колесо теперь не съедается и должно прокручивать.");
        }

        /// <summary>Колесо не зумит и, главное, не съедается — пусть прокручивает.</summary>
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
