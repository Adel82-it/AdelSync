# AdelSync

Контроль обмена **1С ↔ WooCommerce** для [zip-market.biz](https://zip-market.biz).

**От Адель Гайнуллина**

## Что умеет

- Живой статус: идёт обмен / готов / окно защиты
- Спидометры: оценка сэкономленной нагрузки CPU, отбитые ретраи
- Таймер **до следующего доступного полного разбора**
- Всплывашки в трее: «сейчас идёт обмен», «повтор отбит»
- Журнал событий с сайта

## Сборка Windows (GitHub Actions)

Как в проекте ТГК:

1. Запушь репозиторий на GitHub
2. Actions → **Publish AdelSync** → Run workflow  
   или push в `main`
3. Скачай artifact `AdelSync-windows` → `AdelSync.exe`

Локально (на Windows):

```powershell
./ci/publish.ps1
```

## API на сайте

MU-plugin: `wordpress/zip-market-edi-antiretry.php`  
→ на сервере: `wp-content/mu-plugins/zip-market-edi-antiretry.php`

```
GET https://zip-market.biz/wp-json/adelsync/v1/status?key=ВАШ_КЛЮЧ
```

Ключ по умолчанию прописан в плагине и в `%AppData%\AdelSync\settings.json` после первого запуска.

## Cooldown

45 минут на повторный разбор **того же** XML. Дневные выгрузки разрешены.
