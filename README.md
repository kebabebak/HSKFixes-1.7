# HSK Fixes 1.7

Правки, которые нужны Hardcore SK и модам его состава **именно на RimWorld 1.7**. Код самого пака
не меняется — всё делается снаружи.

Без этого мода на 1.7 пак остаётся без своих настроек и без своих патчей, несколько модов теряют
часть своей функциональности, а дерево исследований стирается при прокрутке. Подробнее — в разделе
«Что чинит».

## Требования

**RimWorld 1.7.** Мод рассчитан только на неё; на 1.6 он просто не загрузится.

**Harmony — из Steam Workshop.** Это важно: 1.7 больше не использует Unity и несёт собственную
сборку рантайма .NET (`10.0.10-waylithclr.dev`). Старые версии Harmony на нём отказываются
работать — MonoMod внутри них сверяет версию CoreCLR со списком известных и на незнакомой бросает

```
PlatformNotSupportedException: CoreCLR version 10.0.10 is not supported
```

Это не одна ошибка, а около двухсот мёртвых классов: исключение уносит статические конструкторы
модов целиком. Версия из мастерской (на октябрь 2026 — Harmony 2.4.1) работает.

В списке модов у Harmony будет значок предупреждения: в её `About.xml` версии заявлены по 1.6
включительно. Это ожидаемо, мод загружается и работает.

**Hardcore SK.** Без пака этот мод не делает ничего — он лечит именно его и моды его состава.

**Combat Extended** — не обязателен, но если он установлен, HSK Fixes 1.7 должен стоять **после**
него.

## Установка

1. Положить папку мода в `RimWorld/Mods/` (или сделать на неё junction).
2. Включить мод в списке.
3. Выставить порядок загрузки — см. ниже.

## Порядок модов

Правило в одну строку: **после Combat Extended, до Hardcore SK.**

Мод объявляет это в `About.xml` (`loadAfter CETeam.CombatExtended`, `loadBefore skyarkhangel.HSK`),
но автосортировка не всегда ставит его верно, поэтому порядок стоит проверить глазами. Рабочий
список выглядит так:

```
 1. Harmony
 2. Core
 3. Royalty
 4. Ideology
 5. Biotech
 6. Anomaly
 7. Odyssey
 8. PatchOperationHighlander
 9. HugsLib
10. Allow Tool
11. Adaptive Storage Framework
12. Combat Extended
13. Toggleable Overlays
14. Humanoid Alien Races
15. [sbz] Fridge
16. Animals are fun!
17. HSK Fixes 1.7      <<<
...
    Hardcore SK
    Hardcore SK - Core
    Hardcore SK - DLC
    ... остальные моды пака
```

Точный состав моды с 8-й по 16-ю у вас может отличаться — значение имеет только относительное
положение: Harmony и Core с дополнениями выше, Combat Extended выше, Hardcore SK ниже.

**Почему порядок важен.** Мод делает свою работу из конструктора `Mod`-класса, то есть в тот
момент, когда игра создаёт классы модов по порядку загрузки. Он успевает собрать `Mod`-класс Core SK
и зарегистрировать его **раньше**, чем игра дойдёт до пакового — поэтому упавший конструктор пака
просто не вызывается. Поставьте мод после пака, и этот приём работать не будет.

## Что чинит

**Mod-класс Core SK.** На 1.7 его конструктор падает на первой же строке: он начинается с
`UnityEngine.Application.SetStackTraceLogType`, а этого метода в новом движке нет. Падает — и пак
остаётся без настроек (их читают 17 файлов) и без единого своего патча. Мод собирает этот объект
сам и применяет патчи Core SK классами по одному: у пака там `PatchAll`, который теряет весь
остаток списка из-за одного неудачного класса.

**Патчи, которые отвергает новая Harmony.** Шесть патчей в пяти модах пака объявляют `__instance`
несовместимого типа — 1.6 это прощала, 1.7 нет: `Unrelated classes cannot describe the same non-null
object`. Цена разная: у Survival Tools Lite и Minerals теряется хвост списка патчей, у Skynet,
Rimatomics и Dubs Mint Menus падает весь статический конструктор мода. У Skynet вдобавок уже
поставленный постфикс начинал бросать на каждую пешку и каждую нужду — около 10 000 строк за одну
загрузку сейва. Мод подменяет такие патчи исправленными, сохраняя их логику.

**Дерево исследований.** При прокрутке картинка стиралась: Research Tree SK сдвигает содержимое
через внутренний `GUIClip.Push`, а заглушка Unity в 1.7 это смещение теряет. Переведено на обычный
`GUI.BeginScrollView`.

**Два типа, которых нет нигде.** Пак называет в дефах `CombatExtended.CompProperties_Smoke` и
`CombatExtended.SmokeInhalationImmuneExtension` — их нет ни в одной версии Combat Extended и в
ванилле. Без них деф не загружается целиком.

**Приоритеты роботов** (Misc Robots + Work Tab) — правка на месте, но **пока не работает**, причина
описана в комментарии к `AIRobotWorkTabFix.cs`.

Не путать с [Patch Transition](https://github.com/linyaDev/PatchTransition): тот закрывает
**отсутствие** чужих модов своими заглушками, а этот — поломки **новой версии игры** при полном
составе. Моды независимы и работают вместе.

## Как убедиться, что работает

В логе появятся строки с префиксом `[HSK Fixes 1.7]`, например:

```
[HSK Fixes 1.7] Combat Extended на месте — заглушка не используется.
[HSK Fixes 1.7] патчи Core SK применены: 120 классов.
[HSK Fixes 1.7] Mod-класс Core SK поднят вручную: ...
[HSK Fixes 1.7] прокрутка дерева исследований исправлена: ...
```

Если строк нет вовсе — мод не включён в списке. Лог на 1.7 лежит в
`%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log`.

## Сборка

```
dotnet build Source/HSKFixes17/HSKFixes17.csproj -c Release
```

Проект берёт первый существующий путь к игре; ссылки — `Assembly-CSharp`, `UnityEngine.CoreModule`,
`UnityEngine.IMGUIModule` и `0Harmony`. Сборка кладётся прямо в `Assemblies/`. Игру перед сборкой
надо закрыть, иначе dll заблокирована.

## In English

Fixes that Hardcore SK and the mods it ships need **specifically on RimWorld 1.7**. The pack's own
code is never edited — everything is done from the outside.

Requirements: RimWorld 1.7, Hardcore SK, and **Harmony from the Steam Workshop** — older Harmony
builds refuse to run on 1.7's custom .NET runtime with `CoreCLR version 10.0.10 is not supported`,
which kills the static constructors of roughly two hundred mod classes. Harmony will show a warning
marker because its `About.xml` only declares up to 1.6; that is expected.

Load order: **after Combat Extended, before Hardcore SK** — see the list above. The mod does its
work from its `Mod` constructor, which only beats the pack to it if it loads first.
