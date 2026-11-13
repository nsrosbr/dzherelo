# Наднаціональні організації: джерела для довідкової бази інженерного полку

Дата перевірки: **13 листопада 2026**. Усі адреси нижче відкривались і перевірялись (curl / реальний браузер / web_extract). Де перевірити не вдалося — прямо написано «не перевірено». Нічого не додумано.

---

## 0. Головне

| Що | Де | Стан |
|---|---|---|
| **IMAS повним комплектом** | `mineactionstandards.org` → один ZIP, 41,9 МБ (05.11.2026) + індекс XLSX | ✅ єдине живе повне сховище |
| **API до IMAS** | немає (TYPO3, статичні PDF + sitemap) | ❌ |
| Найкращий машинний доступ серед усіх | ReliefWeb API v2 (JSON, потрібен appname) + UN Digital Library OAI-PMH | ✅ |
| Найбагатші власні рубрики | OSCE Resources Hub (item_type × source × field of work × year) і GICHD (1 153 публікації) | ✅ |
| Найзакритіше | NSO protected / NSDD full text, NLLP, NATO Archives (бот-стіна) | ⚠️ |

---

## 1. Зведена таблиця

| # | Організація | Хост | Точна адреса | Тип доступу | Власні рубрики | Обсяг (оцінка) | Мова | Доступ |
|---|---|---|---|---|---|---|---|---|
| 1 | **NATO / NSO** — публічний портал | `nso.nato.int` | `https://nso.nato.int/nso/home` | HTML (Angular SPA) + **недокументований REST** `POST /nso/nsdd/webapi/api/current-nato-standards/list` | Так: Tasking Authorities / Standards / Terminology / Education | **1 128 позицій** у переліку; лічильник додає «1 066 обкладинок, 1 191 стандарт, 187 SRD» (так на сторінці); фільтр «MINE» → 40 | EN + FR (назви двомовні) | **open (метадані)** / full text — закрито |
| 2 | **NATO** — основний сайт | `nato.int` | `https://www.nato.int/en` | HTML + **sitemap.xml (10,5 МБ)** | Так: news-and-events / multimedia / topics | **47 798 URL** у sitemap (27 664 EN news, 18 276 FR, 1 856 multimedia) | EN, FR + `selectedLocale` (UA немає) | open |
| 3 | **NATO Archives** | `archives.nato.int` | `https://archives.nato.int/` | HTML за JS-бот-стіною (`/challenge`) | не перевірено | не перевірено | EN/FR | open (але автоматично недоступно) |
| 4 | **NATO JALLC LL Portal** | `nllp.jallc.nato.int` | `https://nllp.jallc.nato.int/` | SharePoint, логін | — | — | EN | **closed** (реєстрація) |
| 5 | **UN DPO** — Military Unit Manuals | `pcrs.un.org` | `https://pcrs.un.org/en/military-unit-manuals-and-guidance` | HTML + прямі PDF | Так: Policies / Handbooks / Unit Manuals / Police | ≈35 позицій у переліку (з них 2 інженерні) | EN (+ FR/ES/AR для частини) | open |
| 6 | **UN Peacekeeping** — policy | `peacekeeping.un.org` | `https://peacekeeping.un.org/en/policy-and-guidance` | HTML | Так: operations / protection / … | десятки політик і доктрин | EN | open |
| 7 | **UNMAS** | `unmas.org` | `https://unmas.org/en/document-library` | HTML + фільтри, PDF; скриптовий доступ ріже (403/202) | Так: Year (2007–2026) × Type | **232 документи** | EN (сайт — 6 мов ООН) | open |
| 8 | **ReliefWeb (OCHA)** | `reliefweb.int` | `https://reliefweb.int` · API: `https://api.reliefweb.int/v2/reports` | **REST/JSON v2** (v1 вимкнено, 410) + HTML | Так: reports / disasters / jobs / training / countries | **не підраховано** — без `appname` запит відхиляється; ліміти й пагінація в apidoc | EN + FR/ES/AR | open, **потрібен затверджений `appname`** |
| 9 | **UN ODS** | `documents.un.org` | `https://documents.un.org/` | JS-застосунок, PDF | символи ООН | 1993→ повний текст, 1946–1993 скани | 6 мов ООН (+DE частково) | open, API не документований |
| 10 | **UN Digital Library** | `digitallibrary.un.org` | `https://digitallibrary.un.org` · OAI: `/oai2d?verb=Identify` | **OAI-PMH** + MARCXML (`?of=xm`) | так (Invenio) | не підраховано (пошук віддає MARCXML, заголовок із `Total-Number-Of-Results`) | 6 мов ООН | open |
| 11 | **GICHD** | `gichd.org` | `https://www.gichd.org/publications-resources/publications-and-reports/` | HTML (TYPO3) + sitemap + RSS | Так: Publications & Reports / Insights & Statements / Annual Reports / Equipment catalogue | **1 153 публікації** | EN; частина — **UA**, FR, ES | open |
| 12 | **GICHD** — EO Risk digital resource | `explosive-ordnance-risk-reduction-pub.gichd.org` | `https://explosive-ordnance-risk-reduction-pub.gichd.org/` | HTML (статичний сайт-довідник) | Так | не підраховано | EN | open |
| 13 | **ICRC** — основний сайт | `icrc.org` | `https://www.icrc.org/en` | HTML (Drupal) + sitemap (18 підмап × 2 000 = ≤36 000 URL) | Так: law-and-policy / document / news | ≤36 000 URL | EN, FR, ES, AR, RU, ZH, PT | open |
| 14 | **ICRC** — IHL Databases | `ihl-databases.icrc.org` | `https://ihl-databases.icrc.org/en` | HTML, пошук | **3 БД**: Treaties/States Parties/Commentaries · Customary IHL · National Practice | тисячі записів | EN (+FR/ES/AR/RU/ZH) | open |
| 15 | **ICRC** — International Review | `international-review.icrc.org` | `https://international-review.icrc.org/` | HTML + PDF | Так: issues (№ 930 = грудень 2025), articles | ~930 випусків з 1869; ~90 сторінок лістингу | EN/FR/ES/RU/ZH/AR/FA | open |
| 16 | **ICRC Library** | `library.icrc.org` | `https://library.icrc.org/library/` | каталог, WAF ріже скрипти («Request Rejected», OAI → 503) | так (каталог) | не підраховано | багатомовний | open для людей, automated — ні |
| 17 | **EU / EDA** | `eda.europa.eu` | `https://www.eda.europa.eu/publications-and-data` | HTML (Sitefinity); OData `/api/default/...` → **401** | Так: Factsheets / Magazines / Annual reports / Thematic & policy reports | ~сотні (точно не підраховано; ~13 на сторінку) | EN | open (частина — за запитом) |
| 18 | **EU Publications Office (Cellar)** | `publications.europa.eu` | `https://publications.europa.eu/webapi/rdf/sparql` | **SPARQL/REST** → 200 `application/sparql-results+json` (перевірено запитом) | так (ontology `cdm`) | не підраховано | 24 мови ЄС | open |
| 19 | **OSCE** | `resources.osce.org` (ред. `osce.org`) | `https://resources.osce.org/resources/publications` | HTML (Drupal) + **Typesense** `POST https://prod.search.web.osce.org/multi_search` (публічний search-only ключ у фронтенді) | **Так**: Documents / Publications / Multimedia / Learning & research; `item_type` = factsheet, annual-report, book, study-report, guide-manual-handbook, evaluation-report, decision-declaration, report, statement-speech | лічильник хаба: **91 418** документів, **91 463** публікацій, **28 466** мультимедіа (глобальний, по фасетах — JS) | EN + DE/FR/IT/ES/RU | open |
| 20 | **NDIA** (США, не НАТО) | `ndia.org` | `https://www.ndia.org/` | HTML (SharePoint), логін | новини / події / журнал | — | EN | переважно **closed** (member) |

---

## 2. Картки з доказами

### 2.1 NATO — NSO та NSDD (найважливіше для стандартів)

- **Назва:** NATO Standardization Office, публічний портал + NSDD (NATO Standardization Documents Database).
- **Хост / адреса:** `https://nso.nato.int/nso/home` (публічний), `https://nso.nato.int/nso/nsdd/` (публічний NSDD), `https://nso.nato.int/protected/home/` (закритий).
- **Тип доступу:** Angular SPA; сторінка віддає **лише** «Loading... Please wait» для curl (403 Akamai), у браузері працює. За фронтендом — REST: `https://nso.nato.int/nso/nsdd/webapi/api/current-nato-standards/list`. Перевірено: `GET` → `405 "does not support http method 'GET'"`; `POST {}` → `400 "Your web browser sent an incomplete request."` — тобто ендпоінт живий, але **тіло запиту недокументоване** (потрібен повний фільтр/CSRF). Придатний для експериментів, не для продакшену.
- **Власні рубрики:** Tasking Authorities (CS, MC, LC, CNAD, AVC, DPC, STB) · Standards (List of Promulgated Standards, Search NSDD, Check Updates, Search Contents, AAPs & Templates) · Terminology (NATOTerm guest) · Education and Training.
- **Обсяг (з живого лічильника):** «Number of items found: **1128** and include **1066** Cover(s), **1191** Standard(s) and **187** SRD(s)». Фільтр по слову «MINE» → **40** позицій.
- **Мова:** EN + FR (двомовні назви в таблиці).
- **Доступ:** метадані — **open** (номер, редакція, short/long title, дата промульгації). Повні тексти — **closed**: у публічній таблиці немає жодного посилання на файл, лише колонка «Link To Document (if available)»; завантаження — через `protected` (реєстрація, національні органи).
- **Приклади типів:** STANAG 2036 «LAND MINE LAYING, MARKING, RECORDING AND REPORTING PROCEDURES»; STANAG 2017 «ORDERS TO THE DEMOLITION GUARD COMMANDER AND DEMOLITION FIRING PARTY COMMANDER»; STANAG 2034 «NATO STANDARD PROCEDURES FOR MUTUAL LOGISTIC ASSISTANCE».
- **Примітки:** NSO — секретаріат стандартизації; STANAG/AJP/ATP **не** лежать публічно комплектом. Публічні AJP/ATP трапляються на національних порталах: перевірено `https://assets.publishing.service.gov.uk/media/618e7da28fa8f5037ffaa03f/AJP-3.9_EDB_V1_E.pdf` (200, 2,3 МБ, «ALLIED JOINT DOCTRINE FOR JOINT TARGETING»). Це законний шлях добувати публічні видання НАТО — по одному, з національних сайтів.

### 2.2 NATO — основний сайт і архів

- `https://www.nato.int/en` → 301 з `www.nato.int`; CMS, є **sitemap.xml 10 485 531 байт, 47 798 `<loc>`**: 27 664 `en/news-and-events`, 18 276 `fr/news-and-events`, 1 856 `en/multimedia`. **Тематичні сторінки в sitemap відсутні** — тобто покриття часткове (лише новини/мультимедіа).
- Документні теми (Standardization, NSO, структура) — звичайні HTML-сторінки, без API. Мови: EN/FR + `?selectedLocale=` (uk відсутній).
- **NATO Archives** `https://archives.nato.int/`: віддає `/challenge` («Verifying Your Browser», редірект по колу) — **автоматично не перевірено**, чи є OAI. Для людини сайт, найімовірніше, працює.
- **NLLP** `https://nllp.jallc.nato.int/`: SharePoint із формою логіну (403 для curl, сторінка логіну для web_extract) — **closed**.

### 2.3 UN — інженерна складова миротворчих операцій

- **`https://pcrs.un.org/en/military-unit-manuals-and-guidance`** — головна «полиця» доктрини. Рубрики: *Policies and guidance · Handbooks · Military Unit Manuals · Police/FPU*. Серед них:
  - **2025.05 UN Military Engineer Unit Manual** → `https://pcrs.un.org/sites/default/files/2026-08/2025.05%20Engineer%20Unit%20Manual.pdf` (**перевірено: 200, application/pdf, 11 830 019 байт**).
  - **2025.20 UN Military Explosive Ordnance Disposal (EOD) Unit Manual** (є в переліку; пряма вгадана адреса дала 404 — тягнути посилання зі сторінки).
  - Плюс: 2025.11 IED Threat Mitigation Handbook, 2020 Manual on Ammunition Management, 2024.10 UN Military Manual, 2025.16 C-UAS Guidelines.
  - Мова: EN; частина — FR/ES/AR.
- **`https://peacekeeping.un.org/en/policy-and-guidance`** — публічні політики й доктрини (Capstone Doctrine тощо), HTML, EN.
- Примітка: `pcrs.un.org` віддає 403 на `robots.txt` (CloudFront) — харвест лише з паузами й людським UA.

### 2.4 UNMAS

- **Адреса:** `https://unmas.org/en/document-library` (корінь редиректить на `/en`).
- **Структура:** фасети **Year** (2026: 43, 2025: 36, 2024: 11, 2023: 14, 2022: 10, 2021: 13, 2020: 19, 2019: 18, 2018: 11, 2017: 15, 2016: 29, 2015: 2, 2013: 6, 2012: 1, 2011: 1, 2009: 2, 2007: 1) × **Type** (дослівно зі сторінки: `Publications (34)`, `Publications and Reports 01 (5)`). Пагінація по 10.
- **Обсяг:** «Showing 1-10 of **232** results found» — 232 документи.
- **Доступ:** open, прямі PDF. **Але:** web_extract → 403, curl без правильного UA → 202/0 байт; працює реальний браузер. Для масового завантаження — браузерна сесія.
- **Приклади:** UNMAS Annual Report 2025 (`.../sites/default/files/2026-05/website_annual_report-single_pg.pdf`), UN Mine Action Strategy 2024, NDM-UN29 Summary Report 2026, фактшити місій (UNISFA), EORE-матеріали (OPT).
- **Мова:** EN; сайт має 6 мов ООН; частина документів — AR/FR/ES.

### 2.5 UN ODS і UN Digital Library

- **ODS** `https://documents.un.org/`: JS-застосунок («L O A D I N G…»), повний текст з 1993, скани 1946–1993, 6 мов ООН, частина — DE. **Документованого API немає** (перевірено лише HTML-застосунок).
- **UN Digital Library** `https://digitallibrary.un.org`: **OAI-PMH працює** — `GET /oai2d?verb=Identify` → 200, валідний `<OAI-PMH>`; пошук віддає MARCXML: `/search?p=mine+action&of=xm` → 200, 412 КБ, заголовок `Search-Engine-Total-Number-Of-Results`. Це найчистіший машинний канал до документів ООН (символи, резолюції, звіти секретаріату).

### 2.6 ReliefWeb (OCHA) — єдиний справжній REST серед гуманітарних

- **API:** `https://api.reliefweb.int/v2/reports`. `v1` **вимкнено**: `{"status":410,...,"The API version 'v1' has been decommissioned. Please use version 'v2' instead."}`.
- **Ключ:** потрібен **затверджений `appname`** — без нього `403 AccessDeniedHttpException "You are not using an approved appname"`. Реєстрація — `https://apidoc.reliefweb.int/parameters#appname`. Є OpenAPI-спека, поля, фільтри, пагінація, ліміти.
- **Що віддає:** reports, disasters, countries, jobs, training, sources, blog, topics — JSON over HTTPS.
- **Для полку:** звіти UNMAS/HALO/Danish Demining Group по Україні, ситуаційні звіти OCHA, оцінки потреб. **Мова:** EN + FR/ES/AR у полі `language`.
- **Примітка:** це найкращий «фоновний» канал для гуманітарного пласту (IMAS-суміжні практики, а не самі стандарти).

### 2.7 GICHD

- **Адреса:** `https://www.gichd.org/publications-resources/publications-and-reports/`.
- **Обсяг:** у пагінації прямо: «Displaying results 1 to 10 out of **1153**». Сторінки: `/publications-and-reports/{n}/` (n = 0…115), тобто ~116 запитів на весь каталог.
- **Рубрики:** Publications and Reports · GICHD insights and statements (Insights / Statements) · Annual Reports · Equipment catalogue · Innovation.
- **Мова:** у кожному записі рядок «Available in English, Ukrainian» — **є українські переклади** (напр. «Conflict-related trauma and operational safety in Ukraine's mine action response», «Enhancing safety through adapted PPE in Ukraine», «Guide to developing national mine action standards, 2nd ed.»).
- **Доступ:** open, PDF.
- **Технічно:** TYPO3; `sitemap.xml` — індекс (лише 163 статичні сторінки, детальні публікації **не** в sitemap); `rss` → 200 `application/xml`; `robots.txt` — не обмежує.
- **Субпортал:** `https://explosive-ordnance-risk-reduction-pub.gichd.org/` — 200, цифровий довідник з управління ризиками ВНП.
- **Роль:** GICHD — секретаріат IMAS Steering Group і Review Board (підтверджено на `mineactionstandards.org/about-imas/`), тому це «дім» і стандартів, і супровідної методики.

### 2.8 ICRC

- **`https://www.icrc.org/en`** — Drupal; `sitemap.xml` = індекс на **18** підмап по 2 000 URL → до ~36 000 сторінок; шляхи `/{lang}/document/...` і `/{lang}/content/...`; мови: en, fr, es, ar, ru, zh, pt.
- **IHL Databases** `https://ihl-databases.icrc.org/en` — три БД: «Treaties, States Parties and Commentaries», «Customary International Humanitarian Law», національна практика. Відкрито, HTML, без API.
- **International Review of the Red Cross** — окремий хост `https://international-review.icrc.org/` (на `icrc.org/en/international-review` → 404). Випуски з 1869, останній перевірений — **IRRC No. 930, грудень 2025**; лістинг до `?page=90`; кожен випуск — «Read online» + «Download PDF»; мови EN/FR/ES/RU/ZH/AR/FA.
- **ICRC Library** `https://library.icrc.org/library/` — каталог, але WAF: `Request Rejected`, `oaiopac.pl` → 503. Для людини працює, для харвесту — ні. Опис фондів: `https://www.icrc.org/en/icrc-library` (включно з **колекцією військових настанов** — релевантно для інженерних довідників).
- **Casebook** `https://casebook.icrc.org/` → 403 (бот-стіна), не перевірено.

### 2.9 EU — EDA і Publications Office

- **EDA** `https://www.eda.europa.eu/publications-and-data` (стара `/publications` → 301 сюди). Рубрики: **Factsheets · Publications · Magazines** (Countdown to 2030, вип. 31) **· Annual reports · Thematic and policy reports**. ~13 позицій на сторінку, пагінації в HTML не видно (довантаження скриптом).
- **API:** Sitefinity (Telerik). `GET /api/default/publications` → **401** `{"error":{"code":"Unauthorized","message":"The current user is not allowed access","target":"Telerik.Sitefinity.Web.Api"}}` — API існує, але **не публічний**. Доступ до документів — через сторінку `https://www.eda.europa.eu/who-we-are/transparency/access-to-eda-documents` (запит).
- **Мова:** EN. **Доступ:** open для опублікованого; решта — за запитом.
- **EU Publications Office / Cellar** — `https://publications.europa.eu/webapi/rdf/sparql`, перевірено запитом `SELECT ?s WHERE {?s ?p ?o} LIMIT 1` → 200 `application/sparql-results+json`. Це єдиний повноцінний API для права/стандартів ЄС (24 мови). Для EDA-документів не застосовний, для європейських директив/регламентів (напр., з поводження з вибухонебезпечними відходами, CBRN) — так.

### 2.10 OSCE

- **Адреса:** `https://www.osce.org/resources/...` → **301 на новий хост** `https://resources.osce.org/resources/publications` («OSCE Resources Hub», Drupal).
- **Рубрики (власні, явні):** Documents · Publications · Multimedia · Learning and research. Підрубрики публікацій: Factsheets · Annual reports · Books · Studies and reports · Manuals, guides and handbooks · Evaluation reports. Документи: `item_type=decision-declaration` · `report` · `statement-speech`.
- **Фасети в пошуку:** Item type · Source · Fields of work · Year · Language. Приклад: `?q=mine action&lang=en` → **60 ресурсів**; мови в фасеті: English 60, Deutsch 3, Français 2, Italiano 2, Español 1, Русский 1.
- **Обсяг:** лічильник хаба показує **91 418** (документи) / **91 463** (публікації) / **28 466** (мультимедіа). Лічильник **глобальний** — при фільтрі `item_type` число не змінюється (перевірено), тому по фасетах точний підрахунок можливий лише через пошуковий API.
- **API:** пошук побудований на **Typesense**: `POST https://prod.search.web.osce.org/multi_search`, заголовок `X-TYPESENSE-API-KEY: <публічний search-only ключ, вшитий у фронтенд>`. Ендпоінт відповідає (200; на невідому колекцію — `{"code":404,"error":"Collection not found"}`); **назву колекції** у бандлі знайти не вдалося — потрібен перехоплювач трафіку. Тобто: машинний доступ **є**, але недокументований і крихкий.
- **Sitemap:** немає (`/sitemap.xml` → 404). JSON:API — вимкнено (`/jsonapi` → 404).
- **Приклад типу:** «Statement by the Delegation of Japan on the Ukraine Mine Action Conference 2025, held in Tokyo on 22 and 23 October».
- **Примітка:** OSCE дає переважно політичні заяви й звіти місій (включно з Україною), а не технічні стандарти. Для інженерного полку — контекст і політика, не нормативи.

### 2.11 NDIA (уточнення)

`https://www.ndia.org/` — **National Defense Industrial Association** (США, промислова асоціація, не НАТО і не орган стандартизації). SharePoint; публічно — новини, події, журнал `https://www.nationaldefensemagazine.org/`; матеріали конференцій переважно за логіном. Для довідкової бази полку цінність низька; якщо потрібні саме стандарти НАТО — див. 2.1.

---

## 3. IMAS: де лежить повний комплект і чи є API

**Єдине живе повне сховище — `https://www.mineactionstandards.org/`** (TYPO3, хоститься GICHD, власник — UNMAS; стандарти затверджує IACG-MA).

| Що | Адреса | Перевірено |
|---|---|---|
| **Повний комплект (EN), один ZIP** | `https://www.mineactionstandards.org/fileadmin/uploads/imas/Standards/English_ALL_IMAS/All_IMAS_Documents_05.11.2026.zip` | 200, `application/zip`, **41 917 412 байт**, `last-modified: 2026-11-05` |
| **Індексний XLSX** | `.../English_ALL_IMAS/List_of_all_IMAS_Documents_05.11.2026.xlsx` | 200, 17 172 байт, 78 рядків |
| Перелік на сайті | `https://www.mineactionstandards.org/standards/` | 78 карток; 299 унікальних PDF-посилань |
| Окремий PDF | `.../fileadmin/uploads/imas/Standards/English/IMAS_01.10_Ed.2_Am.12.pdf` | 200, 419 146 байт; схема імені `IMAS_<номер>_Ed.<n>_Am.<n>.pdf` |
| Переклади | `.../Standards/{Ukrainian,Russian,French,Arabic,Spanish,Korean,Persian,Vietnamese}/` | мови підтверджено в лістингу |

**Склад комплекту (з XLSX, станом на 05.11.2026): 43 IMAS + 26 TNMA + 9 T&EP = 78 документів.** Ключові для інженерного полку:

- 07.11 Land release (+ TNMA 07.11/01–04: символіка, KPI, «all reasonable effort»)
- 08.10 Non-Technical Survey · 08.20 Technical Survey · 08.30 Post-clearance documentation · 08.40 Marking EO hazards
- 09.10 Clearance requirements · 09.11 Battle Area Clearance · 09.13 **Building Clearance** · 09.30 EOD · 09.31 IEDD · 09.50 Mechanical land release · 09.60 Underwater survey and clearance
- 10.10 OHS · 10.20 Demining worksite safety · 10.30 PPE · 10.40 Medical support · 10.50 Storage/transport of explosives · 10.60 Accident reporting
- 11.20 Open burning/open detonation · 11.10/11.30 Stockpile destruction
- 12.10 EORE · 13.10 Victim assistance · 14.10 MEAL
- TNMA-«зброєзнавство»: 08.20/01 **ПМН-3** (техопис), 09.30/05 **ЯМ-1(Б)**, 08.20/02 cluster munition remnant survey, 09.30/02 depleted uranium, 09.30/04 FAE
- 04.10 Glossary (термінологія — обов'язкова для єдиного тезаурусу бази)

**Українські переклади:** на сторінці стандартів підтверджено щонайменше для **IMAS 02.10, 08.30, 09.60** (`.../Standards/Ukrainian/...`). Повного UA-комплекту немає — решта лише EN/RU.

**API:** **немає.** Ні REST, ні OAI, ні JSON. Є:
- `sitemap.xml` — індекс TYPO3 (`?sitemap=pages`, `?sitemap=pages_footer`), тобто перелік сторінок;
- `robots.txt` — `Crawl-delay: 10` (поважати: 78 PDF + 78 сторінок ≈ 15 хв);
- передбачувані шляхи `/fileadmin/uploads/imas/Standards/...` — фактично працює як статичне сховище.

**Практичний висновок:** один `curl` на ZIP + XLSX закриває 100 % IMAS. Це найдешевша й найповніша частина всієї довідкової бази.

**Мертві адреси (перевірено 13.11.2026, не використовувати):** `mineaction.org` (DNS є, з'єднання не встановлюється), `e-mine.un.org` (не відповідає). Історичні «доми IMAS» більше не працюють — тільки `mineactionstandards.org`.

---

## 4. Що НЕ перевірено (і чому)

| Об'єкт | Причина | Що робити |
|---|---|---|
| NATO Archives (`archives.nato.int`) — чи є OAI, обсяг фондів | JS-бот-стіна `/challenge`, редірект по колу і в curl, і в керованому браузері | відкрити вручну в людському браузері |
| NSDD — повні тексти, чи справді closed | публічна таблиця без посилань; protected вимагає реєстрації | тестовий акаунт через національний орган |
| NSO REST — тіло POST-запиту | 400 «incomplete request», потрібен повний фільтр + CSRF | перехопити запит у DevTools |
| OSCE Typesense — назва колекції | у мініфікованому бандлі не знайдено | перехопити `multi_search` у DevTools (1 хв) |
| EDA — точна кількість публікацій | лістинг довантажується скриптом, лічильника немає | рахувати сторінки вручну |
| ICRC Library — обсяг каталогу, OAI | WAF: `Request Rejected`, 503 | тільки людиною |
| ICRC Casebook | 403 | вручну |
| UN ODS — API | застосунок JS, документації не знайдено | використовувати UN Digital Library (OAI) |
| ReliefWeb — фактична кількість звітів | без `appname` запит відхиляється (403) | отримати appname, тоді `GET /v2/reports?...&limit=0` дає `totalCount` |

---

## 5. Рекомендації для харвесту (за спаданням віддачі)

1. **IMAS ZIP + XLSX** — 1 запит, 78 документів, повний нормативний фундамент. `Crawl-delay: 10`.
2. **GICHD** — 116 сторінок лістингу `/publications-and-reports/{0..115}/`, з кожної — PDF + поле «Available in …». Дає і стандарти, і методику, і UA-переклади.
3. **UNMAS Document Library** — 232 документи, 24 сторінки; **тільки через браузерну сесію** (скриптовий доступ ріже).
4. **pcrs.un.org** — 2 інженерні мануали (Engineer 2025.05 вже перевірено, 11,8 МБ; EOD 2025.20) + IED Threat Mitigation Handbook. Доктринальна база UN.
5. **ReliefWeb API v2** — після отримання `appname`: безперервний потік звітів по Україні (фільтри `country`, `source`, `format`).
6. **UN Digital Library OAI-PMH** — символи/резолюції/звіти ООН, повний машинний доступ.
7. **OSCE Resources Hub** — тематичні добірки (`q=mine action` → 60) через HTML; Typesense — якщо готові ловити запити.
8. **NATO** — метадані NSDD (1 128 позицій, 40 по «MINE») для покажчика «що існує», а тексти — поштучно з національних порталів (перевірений приклад: AJP-3.9 на gov.uk).
9. **EDA / Cellar** — політика й дані ЄС; для інженерних нормативів другорядне.
10. **NDIA, NLLP, ICRC Library** — пропустити: закриті або нерелевантні.

**Мінімальний набір для «гуманітарного розмінування (IMAS)» + «інженерна справа»:** IMAS (78) + GICHD (1 153) + UNMAS (232) + UN Engineer/EOD manuals (2) = **1 465 документів**; з них українською підтверджено лише частину (3 IMAS + окремі GICHD-публікації), решта — EN/RU.

---

## 6. Технічні деталі, які варто зберегти в базі

- `nato.int/sitemap.xml` — 10,5 МБ, віддається одним файлом; парсити потоково.
- `nso.nato.int` — Akamai: curl → 403, браузер → 200. Харвест лише браузером.
- `resources.osce.org` — `www.osce.org/resources/*` → 301 на новий хост; старі посилання в базі треба переписати.
- `api.reliefweb.int/v1` → 410 (decommissioned). Тільки `/v2`.
- `unmas.org` — 403/202 для скриптів; `unmas.org/en` (не корінь).
- `eda.europa.eu/api/default/*` → 401: Sitefinity API закритий.
- `library.icrc.org` — WAF блокує і каталог, і OAI (503).
- `mineactionstandards.org` — `robots.txt`: `Crawl-delay: 10`.
