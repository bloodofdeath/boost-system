# Вклад в развитие OptiPulse (Contributing)

Мы рады любым улучшениям, исправлениям багов и новым безопасным оптимизациям!

## Принципы проекта

1. **Полная обратимость:** любая оптимизация обязана сохранять исходное состояние через `BackupService` и корректно восстанавливать его в `RevertCore()`.
2. **Без «магии» и плацебо:** мы добавляем только реально работающие и документированные системные параметры Windows.
3. **Никакой телеметрии и скрытых сетевых запросов.**
4. **Чистая архитектура:** системная логика живёт в `OptiPulse.Core`, интерфейс — в `OptiPulse.App`.

## Как добавить новую оптимизацию

1. Создайте класс в `src/OptiPulse.Core/Tweaks/`, унаследованный от `TweakBase`.
2. Заполните `Descriptor` (уникальный `Id`, `Title`, `Description`, `NotRecommendedWhen`, `TechnicalDetails`, `IconKey`, `RequiresAdmin`).
3. Реализуйте `GetStatus()`, `ApplyCore()` (используя `RegistryHelper.BackupAndWrite` / `ServiceControlHelper.CaptureServiceBackup`) и `RevertCore()`.
4. Зарегистрируйте твик в `TweakRegistry.cs` и добавьте иконку в `src/OptiPulse.App/Resources/Icons.xaml`.
5. Добавьте или обновите тесты в `tests/OptiPulse.Tests/` и убедитесь, что `dotnet test` проходит успешно.
