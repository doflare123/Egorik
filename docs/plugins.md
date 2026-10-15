# Кодовые JavaScript-плагины

Egorik Plugin API v1 позволяет интегрировать в приложение настоящий
пользовательский код на JavaScript. Скрипт может использовать функции, условия,
циклы и данные своего пакета, но не получает CLR, shell, сеть, файловую систему,
WinAPI или dependency injection container.

Скрипты исполняются отдельными экземплярами Jint с ограничениями 8 МБ памяти,
500 мс, 20 000 операторов и глубиной рекурсии 64. Каждое системное действие
проходит через capability, объявленную в manifest и реализованную локальным
проверяемым обработчиком Egorik.

## Установка и включение

Пакет помещается в отдельный каталог:

```text
%LocalAppData%\Egorik\plugins\<plugin-id>\
```

Минимальный состав:

```text
plugin.json
plugin.js
```

После перезапуска Egorik пакет появится в разделе **Настройки → Плагины**.
Включение JavaScript-плагина подтверждает отпечаток manifest, кода и файлов
данных. После любого изменения пакета он автоматически перестаёт исполняться до
повторного включения и сохранения настроек.

## Manifest API v1

```json
{
  "id": "example.my-plugin",
  "name": "Мой плагин",
  "version": "1.0.0",
  "apiVersion": "1",
  "kind": "javascript",
  "description": "Описание возможностей плагина для справочника",
  "examples": ["сделай пример"],
  "entryPoint": "plugin.js",
  "dataFiles": ["items.json"],
  "permissions": ["example.capability"]
}
```

Имена файлов не содержат путей. Файлы `.json` доступны скрипту как строки с
именами `data_<имя>`: например, `items.json` превращается в `data_items`.

Необязательные поля `description` и `examples` отображаются во вкладке
**«Справочник»**, включая выключенные плагины. Для декларативных плагинов
используются названия, описания и префиксы команд с примером сущности из каталога.
Для построения справочника код плагина не исполняется. Старые манифесты без новых
полей продолжают загружаться. Встроенный Dota-плагин теперь содержит эти поля;
после обновления потребуется повторно подтвердить его включение из-за изменения
отпечатка манифеста.

Плагин экспортирует две глобальные функции:

```javascript
"use strict";

const items = JSON.parse(data_items);

function match(normalizedText) {
  if (!normalizedText.startsWith("сделай ")) {
    return { matched: false };
  }

  return {
    matched: true,
    confidence: 0.98,
    payload: { value: normalizedText.slice(7) }
  };
}

function execute(payloadJson, normalizedText) {
  const payload = JSON.parse(payloadJson);
  return {
    capability: "example.capability",
    entityId: "example",
    entityName: payload.value,
    value: payload.value
  };
}
```

`match` не выполняет системных действий. `execute` только формирует запрос к
capability. Egorik повторно проверяет permission, обработчик, риск и аргументы.
Добавление новой capability требует отдельного узкого контракта и реализации в
основном приложении.

## Плагин выбора героя Dota 2

Встроенный пакет находится в
`plugins/egorik.dota2.hero-picker`. Логика распознавания команд и словаря героев
реализована в `plugin.js`, а системный ввод — capability `dota2.hero_pick`.

При включении Egorik создаёт собственный файл
`gamestate_integration_egorik.cfg` и принимает Game State Integration только на
`127.0.0.1`. После первого включения запущенную Dota 2 нужно перезапустить.

Перед вводом имени проверяются свежая стадия
`DOTA_GAMERULES_STATE_HERO_SELECTION` и активное окно `dota2.exe`. Затем имя
вводится через Hero Quick Search, карточка открывается Enter, после чего плагин
нажимает кнопку **Выбрать** в правой панели относительно размеров окна и
возвращает курсор на прежнее место. Чтение памяти, DLL-инъекции, консольные
команды и обход античита не используются.

Примеры:

```text
пикни пуджа
возьми баратрума
выбери героя Crystal Maiden
запикай мортру
```
