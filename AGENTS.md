> **Для ИИ-агента.** Человеческое описание — [README.md](README.md) (роль: обзор, сборка, статус, использование).
> Правило ведения: rules/readme-agents-separation.md.

# Fond2009.Shared.csFastFloat — AGENTS.md

**Тип:** shared-библиотека Fond2019 — быстрый корректно-округлённый парсер `float`/`double`
(порт Lemire `fast_float`, MIT). Пакет: `Fond2009.Shared.csFastFloat`.
**Upstream:** https://github.com/CarlVerret/csFastFloat
**Наш форк (GitHub):** https://github.com/AlexanderSemenyak/csFastFloat
**МАП (алгоритм/цикл, математика, проблемы, плюсы, использование):**
`taxonomy/process-algorithms/ALG-csfastfloat-parse.md`
**Домен-раздел cp_fond (CP-00022):** `cp_fond/docs/csfastfloat-fast-parse.md`
**Потребитель-эталон:** Fond2019 — `TreeViewForBD_data/Telemetry/HelperTelemetry.cs`
(`ToDoubleIfPossible`, ~строки 4622–4636)

## Точки входа

| Что | Где |
|-----|-----|
| Проект | `csFastFloat/csFastFloat.csproj` (TFM: `net8.0;net5.0;netcoreapp3.1;net462`) |
| Тесты | `TestcsFastFloat/TestcsFastFloat.csproj` (xunit; `net9.0;net8.0;net472`) |
| Запуск тестов | `dotnet test TestcsFastFloat\TestcsFastFloat.csproj -c Release -f net472` |
| Регрессы парсинга | `TestcsFastFloat/Basic/TestTrailingContent.cs` |
| Сборка (net462) | `dotnet build csFastFloat\csFastFloat.csproj -c Release -f net462` |

## Инварианты (обязательные)

| ID | Инвариант (нарушение = дефект) |
|----|-------------------------------|
| X-1 | **Подпись.** Библиотека, тесты и Benchmark подписаны **ONIT-ключом** `csFastFloat/StrongNameKey.snk` (public key `0024…bba0148c…`, token `6f5d2a7dac106d62`); `InternalsVisibleTo` в `csFastFloat/AssemblyInfo.cs` ссылается на этот ключ. В `TestcsFastFloat.csproj` / `Benchmark.csproj` — **относительный** путь `..\csFastFloat\StrongNameKey.snk`. Абсолютные пути ключа (напр. `D:\…`) запрещены. |
| X-2 | **Prefix-семантика (ловушка).** `TryParseDouble(string, out double)` работает как `strtod`: берёт **длиннейший числовой префикс** и игнорирует хвост. Это **НЕ** `double.TryParse` (BCL требует всю строку). Поведение документировано в README. Хвостовые пробелы: `AllowTrailingWhite` допускает пробелы после числа, но `characters_consumed` их может не поглощать (`"  42  "` → consumed=4 из 6). |
| X-3 | **Guard обязателен.** Использовать overload с `characters_consumed` и проверять полное потребление. Перегрузки БЕЗ `characters_consumed` (`out double`) не позволяют применить guard → **в продуктовом коде запрещены**. ⚠️ Сравнивать с длиной после отбрасывания хвостовых пробелов (`AllowTrailingWhite`), иначе валидное число молча уйдёт в fallback. Fallback `ParseDoubleSimple()` — проверить, что он сам не разбирает префикс (риск R-3). Эталон — `HelperTelemetry.cs:4623,4629`. |
| X-4 | **net462.** Путь скалярный (без `HAS_INTRINSICS`/SSE4.1) — корректность та же, скорость ниже. |
| X-5 | **Правки `.cs`/`.csproj`** — только по явному запросу; для C#-субагента обязателен F-CODERULES-INJECT. |

## Обязательный паттерн вызова (копировать)

```csharp
using csFastFloat;
using System.Globalization;

// decimal_separator = ','
if (FastDoubleParser.TryParseDouble(datas, out var used, out var value,
        NumberStyles.Float, ',') && used == datas.Length)
    return value;

// decimal_separator = '.'
if (FastDoubleParser.TryParseDouble(datas, out var used2, out var value2,
        NumberStyles.Float, '.') && used2 == datas.Length)
    return value2;

// медленный fallback
return datas.ParseDoubleSimple();
```

> Без проверки `used == datas.Length` строка `"17.9.2026 12:18"` (dec `','`) вернёт `true` и `17` —
> тихая порча данных. См. МАП §Проблемы.

## Известные особенности окружения

- **net8.0:** тест `TestcsFastFloat.suppl_tests.Strtod_tests.strtod_tests_file` падает как
  ApprovalTests-mismatch **по локали** (утверждённый baseline содержит `NaN`, фактический вывод на
  ru-RU — `не число`). **Pre-existing**, не связан с парсером (см. `strtod_tests/…ForScenario.8.0.*`).
- **net472 / net462:** полный не-exhaustive прогон зелёный.

## Связанные

- МАП + математика: `taxonomy/process-algorithms/ALG-csfastfloat-parse.md`
- Домен Fond2019: `cp_fond/docs/csfastfloat-fast-parse.md` (CP-00022)
- Кандидат-усиление `hand` (SWAR `parse_eight_digits`): `ALGORITHM-mview-fast-readers.md` O-08
