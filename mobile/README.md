# اسکنر اسناد (موبایل)

برنامهٔ جدا برای اسکن چندصفحه‌ای با دوربین گوشی و ثبت همان فایل، به‌صورت PDF، در بایگانی موجود.

وب (`frontend/`) و سرویس دات‌نت عوض نشده‌اند. این برنامه فقط کلاینت همان API است.

## چه چیزی لازم است

- Node.js 20 یا جدیدتر
- اپ Expo Go روی گوشی، یا یک development build (دوربین در شبیه‌ساز محدود است)
- API در حال اجرا. در توسعه معمولاً `http://localhost:5080`

اندروید و iOS هر دو از همین کد استفاده می‌کنند. پوشه‌های `ios/` و `android/` ساخته نمی‌شوند؛ Expo آن‌ها را موقع build می‌سازد.

## نصب

```bash
cd mobile
npm install
```

اگر npm به‌خاطر نسخه‌های React به تداخل peer برخورد، این شکل کافی است:

```bash
npm install --legacy-peer-deps
```

## متغیر محیط

فایل `.env` را از نمونه بسازید:

```bash
cp .env.example .env
```

فقط همین مقدار لازم است. راز سرور را اینجا نگذارید؛ هر چیزی داخل برنامهٔ موبایل قابل استخراج است.

```env
EXPO_PUBLIC_API_URL=https://example.com/api/v1
```

آدرس باید خودِ پیشوند `/api/v1` را داشته باشد.

| محل اجرا | مقدار مناسب |
|---|---|
| شبیه‌ساز iOS | `http://localhost:5080/api/v1` |
| شبیه‌ساز اندروید | `http://10.0.2.2:5080/api/v1` |
| گوشی روی همان شبکه | `http://<IP رایانه>:5080/api/v1` |
| تولید | فقط `https://` |

برنامهٔ نیتیو محدودیت CORS مرورگر را ندارد. اگر همین پروژه را در مرورگر Expo باز کنید، مبدأ آن باید در `Dms:Cors:Origins` سرور باشد.

## اجرا

```bash
npx expo start
```

از منوی Expo:

```bash
npx expo start --android
npx expo start --ios
```

`ios` فقط روی macOS با Xcode معنی دارد. بدون مک، Expo Go روی آیفون کافی است.

در منوی Expo کلید `w` همان برنامه را در مرورگر باز می‌کند. برای این حالت `react-native-web` لازم است و در وابستگی‌ها هست. دوربین در مرورگر فقط روی `localhost` (یا HTTPS) باز می‌شود.

## اجرا با بقیهٔ سامانه در داکر

همان `docker compose` که وب و API را بالا می‌آورد، اسکنر وب را هم می‌سازد و روی پورت `8091` سرو می‌کند. nginx داخل کانتینر `/api` را به API می‌فرستد، پس آدرس API در بیلد همان `/api/v1` است و CORS لازم نیست. ساخت APK اندروید پیش‌فرض نیست؛ فقط وقتی لازم است:

```bash
cp deploy/.env.example deploy/.env
cd deploy && docker compose up -d --build
docker compose --profile apk build apk
docker compose --profile apk run --rm apk
```

بعد از بالا آمدن: بایگانی روی http://localhost:8090 و اسکنر روی http://localhost:8091. پورت را با `SCANNER_PORT` در `deploy/.env` عوض کنید. APK از QR صفحهٔ ورود در پورت ۸۰۹۰ دانلود می‌شود.
## ساخت با EAS

`eas.json` سه پروفایل دارد: `development`، `preview`، `production`. این مخزن چیزی را منتشر نمی‌کند و به فروشگاه نمی‌فرستد.

```bash
npx eas-cli@latest build --profile preview --platform android
npx eas-cli@latest build --profile production --platform all
```

قبل از build تولید، `EXPO_PUBLIC_API_URL` باید `https` باشد. شناسهٔ بسته: `com.dms.scanner`.

## API

قرارداد همان سرویس فعلی است. مسیرها نسبت به `EXPO_PUBLIC_API_URL` هستند.

| کار | درخواست |
|---|---|
| ورود | `POST /auth/login` با `{ username, password }` |
| تازه‌سازی | `POST /auth/refresh` با `{ refreshToken }` |
| خروج | `POST /auth/logout` با `{ refreshToken }` و هدر Bearer |
| تغییر رمز | `POST /auth/change-password` با `{ currentPassword, newPassword }` |
| پوشه‌ها | `GET /categories` |
| نوع سند | `GET /document-types` |
| فرم نوع سند | `GET /document-types/{id}/schema` |
| آپلود | `POST /uploads` چندبخشی، نام فیلد `file` |
| ثبت سند | `POST /documents` با هدر `Idempotency-Key` |
| مشاهده | `GET /documents/{id}` |

پاسخ ورود و تازه‌سازی: `accessToken`، `accessTokenExpiresAt`، `refreshToken`، `refreshTokenExpiresAt`، `user`.

بدنهٔ ثبت سند: `title`، `description`، `categoryId`، `documentTypeId`، `uploadId`، `tags`، `changeDescription`، `metadata`.

خروجی ثبت: `documentId`، `versionId`، `label`. شمارهٔ سند جداگانه‌ای در API نیست؛ برچسب نسخه (مثل `V1.1`) نشان داده می‌شود.

توکن دسترسی حدود ۱۵ دقیقه اعتبار دارد و تازه‌سازی آن را می‌چرخاند. اگر چند درخواست با هم ۴۰۱ بگیرند، فقط یک بار تازه‌سازی انجام می‌شود و بعد همان درخواست‌ها تکرار می‌شوند. شکست تازه‌سازی نشست را پاک می‌کند.

رمز و توکن در لاگ نوشته نمی‌شوند. توکن‌ها در SecureStore هستند، نه AsyncStorage.

اگر حساب باید رمز را عوض کند (`mustChangePassword`)، سرور هر کاری جز مسیرهای `/api/v1/auth` را با ۴۰۳ رد می‌کند. برنامه همان‌جا فرم تغییر رمز را نشان می‌دهد. حداقل طول رمز در سرور ۱۲ نویسه است. بعد از تغییر، همهٔ نشست‌ها باطل می‌شوند و باید دوباره وارد شوید.

## جریان کار در اپ

بعد از ورود، چهار بخش پایین صفحه دارید:

1. **بایگانی** — فهرست اسناد، فیلتر پوشه، جستجو در عنوان
2. **اسکن** — شروع اسکن چندصفحه‌ای، ساخت PDF، ثبت در بایگانی
3. **جستجو** — جستجوی عنوان و متن داخل فایل
4. **من** — مشخصات کاربر، کارهای در انتظار، تغییر رمز، خروج

جریان اسکن:

1. تب اسکن → دوربین یا گالری
2. مرور، حذف، جابه‌جایی ترتیب صفحات
3. ساخت PDF روی خود گوشی (`expo-print`)
4. آپلود PDF و سپس `POST /documents`
5. صفحهٔ موفقیت، مشاهدهٔ سند، اسکن بعدی، یا بازگشت به بایگانی

از تب «من» می‌توانید کارهای workflow در انتظار را ببینید و سند مربوط را باز کنید. اقدام تأیید/رد روی موبایل نیست؛ برای آن از نسخه وب استفاده کنید.

لبه‌یابی، OCR و اصلاح پرسپکتیو در این نسخه نیست. نوع سند پیش‌فرض «سند عمومی» است و متادیتا ندارد. اگر نوع دیگری فیلد الزامی ساده داشته باشد (متن، عدد، تاریخ، انتخاب، بله/خیر) همان‌ها پرسیده می‌شود. فیلد الزامی از نوع کاربر، گروه یا ارجاع سند در این نسخه پشتیبانی نمی‌شود.

اگر همان فایل را قبلاً دیده باشید، آپلود فهرست تکراری برمی‌گرداند و سند دوباره ساخته نمی‌شود.

## بررسی

```bash
npm test
npm run typecheck
```
