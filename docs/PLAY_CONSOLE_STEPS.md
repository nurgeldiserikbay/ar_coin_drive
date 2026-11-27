# AR Coin Drive — шаги в Google Play Console

Всё, что нужно загрузить и вставить, уже готово. Идите по порядку.

## Файлы

| Что | Где |
|---|---|
| Сборка для закрытого теста (только тестовая реклама) | `build/ARCoinDrive-1.0-4-testing.aab`, versionCode 4 |
| Отладочные символы (для Play, чтобы не было предупреждения) | `build/ARCoinDrive-1.0-4-testing-*.symbols.zip` |
| Иконка 512×512 | `build/store-art/icon-512.png` |
| Баннер 1024×500 (Feature graphic) | `build/store-art/feature-graphic-1024x500.jpg` |
| Скриншоты телефона 1920×1080, по порядку | `build/store-art/screenshot-1.jpg` … `screenshot-8.jpg` |

Скриншоты горизонтальные, так лучше видно гонку. Play принимает и горизонтальные, и вертикальные.

Как пересобрать картинки после изменений графики:
1. Unity `-executeMethod ARCoinDriveRelease.BuildUiCapture -buildTarget Win64`.
2. Запустить `build/uicapture/ARCoinDrive.exe -storeart`. Около минуты игра идёт на весь экран.
3. Выполнить `tools/StoreArtCompose.ps1`.

## 1. Создать приложение

All apps → Create app:
- App name: **AR Coin Drive**
- Default language: **English (United States) – en-US**
- App or game: **Game**; Free or paid: **Free**
- Отметить согласия (Developer Program Policies, US export laws).
- Имя пакета: **`com.thelightcome.arcoindrive`**. Отдельного поля для него нет. Play берёт его из первого загруженного AAB, и после этого его нельзя поменять. Старые сборки `com.arcoindrive.game` (1.0-1…1.0-3) больше не загружайте.

## 2. Store listing (Grow → Store presence → Main store listing)

- **App name:** AR Coin Drive
- **Short description** (до 80 символов):

  ```
  Race tiny RC cars on your floor in AR: loops, jumps, battles and coins.
  ```

- **Full description:**

  ```
  Turn your floor into a race track! AR Coin Drive puts a tiny radio-controlled car in your room and lets you race it around a track that fits your floor.

  RACE
  • 8 tracks in 3 sizes, from a small rug to a whole room
  • Loop-the-loops and jump ramps: steer in the air to spin, land straight for coins
  • Race up to 3 computer rivals over 5 laps, grab coins for a speed boost

  BATTLE
  • Bumper battles in a shrinking ring
  • Ram your rivals with your nose, side hits hurt most
  • Wreck them or push them out to win the round

  PLAY ANYWHERE
  • AR mode on ARCore phones: the car drives on your real floor and bumps into real walls and furniture
  • No AR? Play in a virtual house, backyard or workshop
  • Chase camera, driver's seat or your own phone view

  FRIENDS AND GARAGE
  • Local multiplayer: up to 4 players on the same Wi-Fi or hotspot
  • Buggies, tuners, muscle cars, supercars, rally and off-road cars
  • Upgrade the engine, tyres and weight; pick paint, finish, rims, spoiler and stripes

  The game contains ads. The optional VIP pass removes ads and doubles the coins you earn.
  ```

- **App icon:** `icon-512.png`; **Feature graphic:** `feature-graphic-1024x500.jpg`
- **Phone screenshots:** `screenshot-1.jpg` … `screenshot-8.jpg`, в этом порядке.
- Store settings: Category **Games → Racing**; Tags: Racing, Augmented reality, Casual; Contact email: ваш gmail; Website можно оставить пустым.

## 3. App content (Policy → App content)

| Раздел | Ответ |
|---|---|
| Privacy policy | `https://docs.google.com/document/d/e/2PACX-1vQC9oA5-f4n4TVwz0G8_WTuk_a2wLmmRno5B0NewYHIg4PUXv_c3BSojrKTB8_GQOxlbXIIXtYoDhcB/pub` |
| App access | All functionality is available without special access |
| Ads | **Yes, my app contains ads** |
| Advertising ID | **Yes**, цель: **Advertising or marketing** (рекламный SDK использует AD_ID) |
| Content rating | Анкета IARC: категория **Game**. Мультяшные гонки и столкновения игрушечных машин, крови нет. Азартных игр нет. Общения между игроками нет (игра только по локальной сети, без чата). Покупки в приложении: **да**. Ожидается Everyone / PEGI 3 |
| Target audience and content | Возрастные группы: **13–15, 16–17, 18+** (не младше 13). В программе Families не участвуем |
| Appeals to children | **Нет** |
| News app / COVID / Government / Financial / Health | Нет |
| Data safety | См. ниже |

### Data safety

Быстрый способ: Data safety → **Import from CSV** → `build/data_safety_arcoindrive.csv`. В нём уже стоят все ответы ниже, останется проверить и нажать Save. Вручную:

- Does your app collect or share user data? **Yes**.
- All data encrypted in transit? **Yes**.
- Way to request deletion? **No**: аккаунтов нет, данные игры хранятся на устройстве и удаляются вместе с приложением.
- Собираемые данные. Для всех ставим: **Collected** и **Shared**, сбор **required** (пользователь не может его отключить). Цели: **Advertising or marketing**, **Analytics**, **Fraud prevention, security and compliance**.
  - **Location → Approximate location** (по IP-адресу, через AdMob)
  - **App activity → App interactions** (AdMob)
  - **App info and performance → Diagnostics** (AdMob)
  - **Device or other IDs** (рекламный ID, AdMob)
- **Financial info → Purchase history**: Collected, не Shared, цель **App functionality** (покупки через Google Play).
- Камера: изображения обрабатываются только на устройстве и никуда не отправляются, поэтому в Data safety её **не указываем**.

## 4. Товары (Monetize → Products → In-app products)

Product ID вводите точно так, маленькими буквами. Тип у всех трёх: разовая покупка (не подписка).

| Product ID | Name | Description | Цена |
|---|---|---|---|
| `vip_pass` | VIP Pass | No ads and double coins forever. | ~$3.99 |
| `car_stinger` | Stinger | Premium sports car for your garage. | ~$2.99 |
| `car_apex` | Apex | Premium supercar for your garage. | ~$4.99 |

Цены можно поставить свои: игра показывает цену из Google Play. Затем нажмите **Activate** у каждого товара.

## 5. AdMob

1. **Privacy & messaging → GDPR (European regulations)**: создайте сообщение для приложения AR Coin Drive и опубликуйте его. Без него игроки из ЕЭЗ и Великобритании не увидят окно согласия, и реклама им показываться не будет.
2. После публикации в Play: **Apps → App settings → Add app store details**, привяжите приложение к странице в Google Play.

## 6. Закрытое тестирование (Test and release → Testing → Closed testing)

1. Create track (или используйте «Closed testing – Alpha»).
2. Testers: создайте список email минимум из **12 человек**, сохраните ссылку для участия и разошлите её.
3. Countries: выберите страны.
4. Create release → загрузите `ARCoinDrive-1.0-4-testing.aab`. При первой загрузке согласитесь на **Play App Signing**: Google хранит ключ подписи приложения, а наш ключ TheLightCome становится ключом загрузки (тот же, что у Fruit Tree).
5. Символы: после загрузки откройте App bundle explorer → выберите версию 4 → Downloads → **Native debug symbols** → загрузите `.symbols.zip`. Предупреждение про deobfuscation file можно игнорировать: R8/ProGuard не используется.
6. Release name: `1.0 (4) test ads`; Release notes:

   ```
   <en-US>
   First test build. Ads in this build are Google test ads.
   </en-US>
   ```

7. Review release → Start rollout. После проверки Google тестировщики смогут установить игру. Тест должен идти **14 дней** с 12+ активными участниками, только после этого откроется заявка на продакшн.
8. License testing (Settings → License testing): добавьте свой gmail, чтобы проверять покупки без списания денег.

## 7. После закрытого теста — продакшн

1. Скажите мне, и я соберу **Production AAB** с настоящей рекламой и **versionCode 5** (`ARCoinDriveRelease.BuildBundle`).
2. Apply for production → загрузите его в Production.
3. В AdMob привяжите приложение к Play (шаг 5.2). Если указан сайт разработчика, добавьте на него `app-ads.txt` (строку покажет AdMob).
