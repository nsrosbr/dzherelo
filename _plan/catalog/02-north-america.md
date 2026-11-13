# Північна Америка (США, Канада): сховища військово-інженерних документів

Дата перевірки всіх адрес: **13.11.2026**. Перевірка = фактичне завантаження сторінки (web_extract / curl / релей r.jina.ai).
Позначки перевірки: **[ок]** сторінка відкрилась і віддався текст · **[частково]** відкрилась, але PDF/підсторінки автоматично не завантажуються · **[ні]** не відкрилась (мережа/файрвол/404) — адреса не підтверджена.

---

## США

### 1. Army Publishing Directorate (APD) — головний реєстр публікацій Армії США
- **Хост:** armypubs.army.mil
- **Адреса:** https://armypubs.army.mil/ ; карта серій: `ProductMaps/PubForm/<SERIES>.aspx` (ATP, ATTP, FM, TC, ADP, ADRP, STP, GTA, TM…)
- **Перевірка:** **[ок]** (прямий доступ з цієї мережі не працює — DNS не резолвиться; сторінка і PDF-и читаються через релей r.jina.ai). Підтверджено живі карти: `.../ATTP.aspx`, `.../ATP.aspx`, `.../FM.aspx`, `.../TC.aspx`.
- **Тип доступу:** HTML (ASP.NET pub maps, постбек-таблиці) + static PDF (`armypubs.army.mil/epubs/DR_pubs/...`, `.../epubs/Misc/...`). OAI/REST/sitemap немає.
- **Власна структура рубрик:** так, за серіями (ADP/ADRP/ATP/ATTP/FM/TC/TM/TB/GTA/STP/CTA) + пошук по номеру/назві + "Army Publishing Index" (активні/скасовані).
- **Оцінка інженерних документів:** ~20–30 активних у серії 3-34 (ATP/ATTP/FM/TC) + десятки TM/TB інженерної тематики (5-3xx, 5-4xx) + інженерні позиції в інших серіях (3-90.4, 3-90.8, 3-37.34).
- **Мова:** en.
- **Доступ:** open; частина позицій CUI/«CAC required» (напр. ATP 3-90.4 «COMBINED ARMS MOBILITY» позначена (CUI)).
- **Приклади (перевірені по номерах/PUB_ID):**
  - FM 3-34 *Engineer Operations*, ред. 15.12.2025 (PUB_ID 1032275)
  - ATP 3-34.10 *Engineer Platoons* (2021); ATP 3-34.22 *Engineer Operations — BCT and Below* (2021)
  - ATP 3-34.40 *General Engineering* (2023); ATP 3-34.81 *Engineer Reconnaissance* (2016); ATP 3-34.80 *Geospatial Engineering* (2024)
  - TC 3-34.85 *Sapper Leader Course Handbook* (2023); TC 3-34.200 *Bridge Crewmember Qualification Tables* (2021); TC 3-34.500 *Construction Qualification Tables* (2020)
- **Примітки:** депо-джерело для всіх 3-34; сторінки деталей типу `ProductMaps/PubForm/Details.aspx?PUB_ID=...`; сторінка ATTP рендериться JS-таблицею (у текстовому витягу рядків немає). Через мережеві блоки краще тягнути через релей або з .mil-мережі.

### 2. Central Army Registry (CAR)
- **Хост:** atiam.train.army.mil
- **Адреса:** https://atiam.train.army.mil/catalog/dashboard
- **Перевірка:** **[ні]** — таймаут рендерингу (сторінка за логіном).
- **Тип доступу:** REST/JS-каталог за авторизацією (CAC/A365).
- **Структура:** каталог армійських публікацій з версіями; публічний доступ обмежений.
- **Оцінка:** ~повне дзеркало APD + CUI-версії; інженерних документів стільки ж, скільки в APD.
- **Мова:** en. **Доступ:** closed (логін).
- **Примітки:** саме CAR згадується на титулах бойових статутів як друге офіційне місце публікації (напр. ATP 3-34.45/MCRP 3-40D.17).

### 3. U.S. Army Engineer School + Maneuver Support Center of Excellence (Fort Leonard Wood)
- **Хост:** home.army.mil (гарнізонний портал FLW)
- **Адреса:** https://home.army.mil/wood/units-tenants/usaes ; довідкові видання: https://home.army.mil/wood/contact/publications ; архів журналу: https://home.army.mil/wood/contact/publications/engr_mag/Engineer-Archives
- **Перевірка:** **[ок]** (обидві сторінки відкрились і віддали текст/навігацію).
- **Тип доступу:** HTML (Drupal-гарнізон) + static PDF (архіви бюлетенів).
- **Власна структура рубрик:** так — MSCoE Professional Bulletins: *Engineer*, *Protection*, *Army Chemical Review*, *Military Police*; підрубрики USAES (історія, школи, Sapper Leader Course, музей, historian).
- **Оцінка інженерних документів:** 1 журнал (Engineer PB) з повним архівом (сотні статей/випусків) + десятки навчальних/доктринальних матеріалів школи; самі статути лежать не тут, а в APD.
- **Мова:** en. **Доступ:** open.
- **Приклади:** *Engineer* Professional Bulletin (архів випусків); статті рівня «Challenges of LSCO at the Engineer Company Level»; матеріали Sapper Leader Course; USAES Historian.
- **Примітки:** згадується «Engineer School Knowledge Network» (окремий закритий/напівзакритий ресурс, не перевірено). Тут же — Engineer Museum.

### 4. CALL → TLLM-Army (Center for Army Lessons Learned)
- **Хост:** army.mil (після реорганізації); старий call.army.mil **не працює**
- **Адреса:** https://www.army.mil/tllm-army/
- **Перевірка:** **[ок]** нова адреса; **[ні]** https://call.army.mil (DNS резолвиться на call.leavenworth.ARMY.mil, 143.84.167.11, але TCP 443 не відкривається — сайт за закритою мережею/переїхав).
- **Тип доступу:** HTML + static PDF (публічно релізні CALL-продукти також на `api.army.mil/e2/c/downloads/...`).
- **Структура:** так — списки публікацій за фіскальними роками з номерами (напр. 26-1205), бюлетені, news distributions.
- **Оцінка інженерних документів:** десятки (CALL-бюлетені/збірки спостережень за mobility, countermobility, EOD/C-IED, base defense), але тематика розсіяна, не інженерний рубрикатор.
- **Мова:** en. **Доступ:** open для публічно релізних; частина — CAC login.
- **Приклади:** CALL Bulletin 23-755 *Integrating CAO in LSCO*; 23-823 *The Journey Toward Total Army Readiness*; 22-05; CTC Trends-випуски (частина CAC).
- **Примітки:** на сторінці прямо вказано: CALL перейменовано в **TLLM-Army** (CAC-TID), старий сайт редиректить; історичний архів CALL збережено. call.army.mil з цієї мережі недоступний — для доступу потрібна .mil-мережа.

### 5. DTIC — Defense Technical Information Center
- **Хост:** discover.dtic.mil / apps.dtic.mil
- **Адреса:** https://discover.dtic.mil/ ; техзвіти: https://discover.dtic.mil/technical-reports/ ; публічний пошук: https://discover.dtic.mil/public-access-search/
- **Перевірка:** **[ок]** (усі три сторінки відкрились); **[ні]** завантаження PDF: `apps.dtic.mil` віддає 403, а сторінка PDF показує «Our Site is Getting an Upgrade / Under Maintenance» — публічний пошук тимчасово вимкнено (так написано і на самому сайті).
- **Тип доступу:** HTML-портал; публічний пошук — JS (Dimensions for DTIC); прямі PDF: `https://apps.dtic.mil/sti/tr/pdf/AD<номер>.pdf`. **Публічного REST/OAI API не документовано** — API/повний доступ через R&E Gateway (https://www.dtic.mil/, CAC/PIV).
- **Структура:** так (Technical Reports / Public Access Search / IACs), але зараз деградована через maintenance.
- **Оцінка інженерних документів:** тисячі (TR/ARL/ERDC/CERL/CRREL звіти: ґрунти, бетон, мости, вибухи, водопостачання); інженерний зріз — сотні.
- **Мова:** en. **Доступ:** public-частина open (зараз offline); CUI/limited — closed (Gateway).
- **Приклади:** ARL-SR-0469 (2023); техзвіти серії AD-A/AD1xxxxxx (напр. ADA578824); ERDC/CERL/CRREL техзвіти.
- **Примітки:** AD-номери — стабільні ідентифікатори; поки сайт у maintenance, пошук краще вести через Google `site:apps.dtic.mil <тема>` і брати AD-номер, а PDF тягнути після відновлення.

### 6. USACE Publications (Engineer Manuals / Regulations / Circulars)
- **Хост:** publications.usace.army.mil
- **Адреса:** https://www.publications.usace.army.mil/ ; EM: `.../USACE-Publications/Engineer-Manuals/`
- **Перевірка:** **[ок]** — індекс EM віддався таблицею з прямими PDF-лінками (`/Portals/76/...`).
- **Тип доступу:** HTML-таблиці з пагінацією + static PDF. OAI/REST немає.
- **Власна структура рубрик:** так — Engineering Regulations (ER), Engineering Circulars (EC), Engineering Manuals (EM), Engineer Pamphlets (EP), техлітери.
- **Оцінка інженерних документів:** 100+ EM (на першій сторінці індексу 50 записів, 17 унікальних номерів) + сотні ER/EC/EP.
- **Мова:** en. **Доступ:** open (частина ER — «For Official Use Only»/CAC).
- **Приклади:** EM 5-1-11 *Project Delivery Business Process*; EM 200-1-2 (екологічні); серії EM 1110-2-xxx (гідротехніка, дамби, берегоукріплення), EM 1110-1-xxx (ґрунти/геотехніка).
- **Примітки:** найкраще джерело для *будівельної* інженерії (не бойової): мости, водопостачання, аеродроми, бетон. Тут же — USACE-стандарти проєктування, які використовують і військові.

### 7. ERDC Knowledge Core (Engineer Research and Development Center)
- **Хост:** erdc-library.erdc.dren.mil
- **Адреса:** https://erdc-library.erdc.dren.mil/
- **Перевірка:** **[ок]** головна відкрилась (DSpace, банер USG); **[ні]** OAI-ендпоінт `?verb=Identify` — WAF «Verifying Session» (бот-захист, не помилка адреси).
- **Тип доступу:** DSpace (HTML-каталог, бітстріми PDF); OAI-PMH імовірно `/oai/request` — **наявність не підтверджена** (заблоковано WAF).
- **Структура:** так — DSpace communities/collections (ERDC, CERL, CRREL, CHL, GSL, ITL).
- **Оцінка інженерних документів:** тисячі звітів, з них інженерно-бойові (дороги, мости, маскування, вибухові роботи, холодні регіони) — сотні.
- **Мова:** en. **Доступ:** open (публічні техзвіти), частина — restricted.
- **Приклади:** техзвіти CRREL (мерзлота, дороги, сніг); CERL (будівлі, енергетика); CHL (гідравліка, переправи).
- **Примітки:** для автоматизації потрібен браузер із «людським» фінгерпринтом; curl/екстрактори б'ються в WAF.

### 8. USAWC Press (U.S. Army War College Press) + Parameters
- **Хост:** press.armywarcollege.edu (bepress/Digital Commons)
- **Адреса:** https://press.armywarcollege.edu/ ; монографії: `.../monographs/`; журнал: `.../parameters/`
- **Перевірка:** **[ок]** головна і підрозділи відкрились (`/series/` — 404, такої рубрики немає).
- **Тип доступу:** Digital Commons: HTML + OAI-PMH (`/do/oai/`), атом-фіди, статичні PDF.
- **Структура:** так — Books, Monographs, Conference Reports, Parameters (журнал), тематичні колекції.
- **Оцінка інженерних документів:** мало (1–5%); тематика — стратегія, логістика, інфраструктура, стійкість. Інженерія присутня опосередковано (mobility/sustainment, infrastructure resilience).
- **Мова:** en. **Доступ:** open (PDF безкоштовні).
- **Приклади:** монографії серії USAWC Press; *Parameters* (статті з логістики/інфраструктури); conference reports.
- **Примітки:** найзручніше джерело з OAI серед американських військових видавництв (bepress дає стандартний OAI).

### 9. Army University Press
- **Хост:** armyupress.army.mil
- **Адреса:** https://www.armyupress.army.mil/
- **Перевірка:** **[ок]** — навігація віддалась (Books, Book Series, Archives, LSCO Book Set…).
- **Тип доступу:** HTML + static PDF (`/Portals/7/...`).
- **Структура:** так — Books / Book Series / Large-Scale Combat Operations Book Set / журнали (Military Review, NCO Journal) / відео.
- **Оцінка інженерних документів:** одиниці (переважно військова історія, лідерство, LSCO); інженерія — окремі розділи/статті.
- **Мова:** en. **Доступ:** open.
- **Приклади:** LSCO Book Set (томи за функціями), *Military Review*, *NCO Journal*, книги серій.
- **Примітки:** корисне для контексту (як інженерні війська вбудовуються в LSCO), не для статутів.

### 10. Marine Corps — MCPEL (Publications Electronic Library)
- **Хост:** marines.mil
- **Адреса:** https://www.marines.mil/News/Publications/MCPEL/ ; пошук по серії: `.../MCPEL/Search/MCWP/`; PDF: `https://www.marines.mil/Portals/1/Publications/<DOC>.pdf`
- **Перевірка:** **[ок]** — MCPEL відкрився (фільтри Type/Sponsor/Category, Tag-сторінки); **[ок]** прямий PDF MCRP 3-40D.17 завантажився і розпарсився (титул+зміст).
- **Тип доступу:** HTML-каталог (JS-фільтри, теги) + static PDF.
- **Структура:** так — по типу (Doctrine/Orders/Directives), спонсору, категорії, статусу (Current/Superseded), є теги по темах.
- **Оцінка інженерних документів:** ~10–15 (MCTP/MCWP/MCRP 3-34x, 3-40D.x) + спільні армійсько-морпіхотні (ATP 3-34.x = MCRP).
- **Мова:** en. **Доступ:** open для publicly releasable; CUI — через TDM-PUBS (`app.mcboss.usmc.mil`, логін).
- **Приклади:** MCWP 3-34 (W/CH-1) *Engineering Operations*, PCN 143 000044 02; MCTP 3-40D (MCWP 3-17.7) *General Engineering* (квіт. 2023); MCTP 3-34A *Combined Arms Mobility* (=ATP 3-90.4); MCTP 3-34B *Combined Arms Countermobility* (=ATP 3-90.8); MCRP 3-40D.17 *Electric Power Generation and Distribution*.
- **Примітки:** більшість інженерних документів USMC — спільні з Армією (той самий текст, подвійна нумерація) — не дублювати в базі.

### 11. Marine Corps Engineer School (MCES), Camp Lejeune
- **Хост:** mces.marines.mil
- **Адреса:** https://www.mces.marines.mil/
- **Перевірка:** **[ні]** — заблоковано (SSRF/мережа). PDF типу `mces.marines.mil/Portals/239/Docs/Doctrine/…placemat.pdf` знайдено в індексі, але **не підтверджено**.
- **Тип доступу:** HTML (імовірно).
- **Оцінка:** невідомо; за індексом — «ENGINEER PUBS WITH CURRENT ACTIONS» placemat (перелік інженерних публікацій USMC у роботі).
- **Доступ:** open/невідомо. **Мова:** en.
- **Примітки:** перспективне джерело для відстеження нових MCTP, але адресу треба перевірити з .mil-мережі.

### 12. Navy — NAVFAC
- **Хост:** navfac.navy.mil
- **Адреса:** https://www.navfac.navy.mil/
- **Перевірка:** **[ок]** головна відкрилась.
- **Тип доступу:** HTML (DVIDS-новини, оргструктура); технічні норми — не тут, а в UFC/UFGS на WBDG.
- **Структура:** ні (це командування, не бібліотека).
- **Оцінка інженерних документів:** 0–5 власне тут; критерії проєктування — через WBDG.
- **Мова:** en. **Доступ:** open.
- **Примітки:** для військового будівництва реальне джерело — UFC/UFGS (п.13).

### 13. WBDG — UFC / UFGS (Unified Facilities Criteria, DoD)
- **Хост:** wbdg.org (Whole Building Design Guide, NIBS)
- **Адреса:** https://www.wbdg.org/ffc/dod/unified-facilities-criteria-ufc ; специфікації: `.../unified-facilities-guide-specifications-ufgs`
- **Перевірка:** **[частково]** — HTTP 200 і заголовок сторінки віддаються, але контент рендериться JS, у markdown не витягується (порожній).
- **Тип доступу:** HTML+JS, PDF по серіях (UFC 1-xxx/3-xxx/4-xxx, UFGS divisions).
- **Структура:** так — UFC за дисциплінами (структурні, механічні, електричні, цивільні), UFGS за MasterFormat.
- **Оцінка інженерних документів:** UFC ~200+ документів, UFGS — сотні розділів; прямо стосуються будівництва баз, укриттів, захисту споруд.
- **Мова:** en. **Доступ:** open.
- **Приклади:** UFC 4-010-01 (мінімальні вимоги захисту від тероризму/вибухів), UFC 3-xxx (структури/цивільні), UFGS 31/32/33-розділи (земляні роботи, бетон, водопостачання).
- **Примітки:** де-факто обов'язкові для проєктування військових об'єктів США; не «статут», але інженерно-нормативна база. Найкраще тягнути через API/браузер.

### 14. NHHC — Seabee manuals (Naval History and Heritage Command)
- **Хост:** history.navy.mil
- **Адреса:** https://www.history.navy.mil/research/library/online-reading-room.html (розділ Seabee: `.../content/dam/museums/Seabee/Online Reading Room/Manuals and Publications/...`)
- **Перевірка:** **[ок]** Online Reading Room відкрився; PDF Seabee Magazine — в індексі, окремо не качав.
- **Тип доступу:** HTML-каталог + static PDF (шлях з пробілами — URL-енкодити).
- **Структура:** так — Library → Online Reading Room → колекції (Seabee manuals/publications, доктринальні тексти флоту).
- **Оцінка інженерних документів:** десятки (Seabee Magazine архів, посібники будівельних батальйонів, історичні manuals).
- **Мова:** en. **Доступ:** open.
- **Приклади:** *Seabee Magazine* (2008–2010+ випуски), Seabee «Manuals and Publications», історичні флотські посібники.
- **Примітки:** історичний/довідковий, не чинна доктрина; сучасні Seabee-норми — в UFC/NAVFAC.

### 15. Air Force e-Publishing (DAF publications)
- **Хост:** e-publishing.af.mil / static.e-publishing.af.mil
- **Адреса:** https://www.e-publishing.af.mil/ ; індекс: `.../Product-Index/`; карта сайту: `.../Site_Map/`
- **Перевірка:** **[ок]** головна та Product-Index відкрились (через релей); **[ні]** static-PDF: 403 Akamai для автоматичного запиту (один із індексованих AFH-шляхів віддав 404 — PDF переїхав/скасований).
- **Тип доступу:** HTML-каталог + static PDF `https://static.e-publishing.af.mil/production/1/<org>/publication/<doc>/<doc>.pdf`.
- **Структура:** так — Product Index за спонсором (у т.ч. **Air Force Civil Engineer Center**), типом (DAFI/DAFMAN/AFH/AFTTP/AFPAM), Site Map, Obsolete Products.
- **Оцінка інженерних документів:** десятки (серія 32-xxxx — civil engineering; AFH 10-222 томи — bare base).
- **Мова:** en. **Доступ:** open (без CAC) для більшості; частина — FOUO/controlled.
- **Приклади:** AFH 10-222 Vol. 3/4/12 (*Guide to Bare Base…* — планування, зведення споруд, механічні системи); AFMAN 32-xxxx (CE); AFTTP 3-2.75 *Military Diving Operations* (спільний з ATP 3-34.84).
- **Примітки:** прямі PDF захищені Akamai — з браузера працює, з скрипта ні. Для масового збору — Product Index + ручне завантаження.

### 16. AFCEC — Air Force Civil Engineer Center
- **Хост:** afcec.af.mil
- **Адреса:** https://www.afcec.af.mil/
- **Перевірка:** **[ок]** (через релей; прямий екстрактор блокує як «private network»).
- **Тип доступу:** HTML (новини/оргструктура), документи — на e-publishing.
- **Структура:** ні (командування). **Оцінка:** 0–5 власне тут.
- **Мова:** en. **Доступ:** open.
- **Примітки:** точка входу в тематику CE (аеродроми, паливні системи, енергетика), але бібліотека — e-Publishing + UFC.

### 17. National Academies Press (NAP)
- **Хост:** nap.nationalacademies.org
- **Адреса:** https://nap.nationalacademies.org/ ; читання: `https://nap.nationalacademies.org/read/<ID>`
- **Перевірка:** **[ок]** — каталог (856 сторінок пагінації) і конкретна книга `/read/5021` відкрились.
- **Тип доступу:** HTML-каталог + безкоштовні PDF після реєстрації; є пошук і API-подібні сторінки `/openbook/...`.
- **Структура:** так — тематичні розділи (Transportation, Infrastructure, Security…), серії (TRB, NCHRP).
- **Оцінка інженерних документів:** сотні дотичних (вибухозахист, мости, тунелі, водопостачання, стійкість інфраструктури); суто військово-інженерних — десятки.
- **Мова:** en. **Доступ:** open (PDF безкоштовні; паперові — paid).
- **Приклади:** *Protecting Buildings from Bomb Damage* (1995); *Blast-Resistant Highway Bridges: Design and Detailing Guidelines* (NCHRP); *Protecting People and Buildings from Terrorism*.
- **Примітки:** єдине велике наукове сховище з безкоштовними PDF; добре для проєктування укриттів/захисту споруд.

### 18. NDU Press (National Defense University)
- **Хост:** ndupress.ndu.edu
- **Адреса:** https://ndupress.ndu.edu/
- **Перевірка:** **[ок]**.
- **Тип доступу:** HTML + static PDF (JTFs, Strategic Forums, books).
- **Структура:** так — публікації/серії (NDU Press books, Joint Force Quarterly, Strategic Forum).
- **Оцінка інженерних документів:** мало (0–5%); інфраструктура/логістика — дотично.
- **Мова:** en. **Доступ:** open.
- **Примітки:** корисно для стратегічного контексту, не для інженерних процедур.

### 19. TRADOC
- **Хост:** tradoc.army.mil / adminpubs.tradoc.army.mil
- **Адреса:** https://www.tradoc.army.mil/ ; пабліки: https://adminpubs.tradoc.army.mil/
- **Перевірка:** **[ні]** — обидва домени не резолвляться з цієї мережі (і через релей теж: «Domain could not be resolved»). Існування сайту підтверджено лише пошуковими сніпетами → **не перевірено**.
- **Тип доступу:** HTML (імовірно) + static PDF.
- **Оцінка:** TP (TRADOC Pamphlets), TR 350-70 тощо; інженерних — одиниці (підготовка, а не інженерія).
- **Доступ:** open/невідомо. **Мова:** en.
- **Примітки:** доктрина армії США зараз видається через APD (п.1), TRADOC — переважно навчальні/програмні документи. Не витрачати зусиль, поки APD доступний.

---

## Канада

### 20. Canadian Army Doctrine (Line of Sight)
- **Хост:** canada.ca (DND/CAF)
- **Адреса:** https://www.canada.ca/en/army/services/line-sight/doctrine-tactics/doctrine.html ; хаб: `.../services/line-sight.html` ; PDF: `https://www.canada.ca/content/dam/dnd-mdn/army/...`
- **Перевірка:** **[ок]** — сторінка доктрини і хаб Line of Sight відкрились, перелік нових B-GL читається.
- **Тип доступу:** HTML (AEM) + static PDF (`/content/dam/dnd-mdn/...`). OAI/REST немає; є внутрішній пошук канадського уряду.
- **Структура:** так — Doctrine / Shield / Sustain / TEWTs & Vignettes / PME-категорії; перелік «Newly Approved Canadian Army Doctrine».
- **Оцінка інженерних документів:** ~5–15 (B-GL-3xx серії; інженерні — B-GL-361/363-подібні, плюс doctrine notes).
- **Мова:** en + fr (окремі версії).
- **Доступ:** open (публічні B-GL); частина доктрини непублічна.
- **Приклади:** B-GL-363-002/FP-001 *CBRND in Land Operations*; B-GL-350-052/FP-001 *Line Planning Guidance*; **CADN-NDAC 20-05 Assault Pioneer Platoon Doctrine Note** (найближче до саперної тематики); B-GL-310-000/FP-001 *Command: The Operational Function*.
- **Примітки:** канадська інженерна доктрина публікується нерівномірно; більшість B-GL недоступні публічно (лише через DWAN/CAF). Старіші B-GL осідають в archive.org та publications.gc.ca.

### 21. Government of Canada Publications (депозитарій)
- **Хост:** publications.gc.ca
- **Адреса:** https://publications.gc.ca/site/eng/home.html
- **Перевірка:** **[ок]**.
- **Тип доступу:** HTML-каталог + PDF/HTML повних текстів; є OAI? — не підтверджено, є RSS/пошук.
- **Структура:** так — за відомством (National Defence), темою, датою; каталог з ISBN.
- **Оцінка інженерних документів:** десятки (старі B-GL, звіти DND, дослідження DRDC у депозиті).
- **Мова:** en + fr. **Доступ:** open.
- **Приклади:** старі B-GL серії (напр. B-GL-300-004 *Land Force Sustainment*, 1999); звіти National Defence; дослідницькі публікації.
- **Примітки:** найкращий легальний шлях до історичних канадських статутів; нові — на canada.ca.

### 22. DND/CAF — Reports and Publications (хаб)
- **Хост:** canada.ca
- **Адреса:** https://www.canada.ca/en/department-national-defence/corporate/reports-publications.html
- **Перевірка:** **[ок]**.
- **Тип доступу:** HTML-хаб, що веде на підкаталоги (Boards of Inquiry, DRIDP, звіти, плани).
- **Структура:** так — за типами звітів/відомствами.
- **Оцінка інженерних документів:** 5–20 (інфраструктура, екологія, будівництво баз; не статути).
- **Мова:** en + fr. **Доступ:** open.
- **Примітки:** DRDC-звіти (defence research) — на цьому ж порталі, окремої стабільної «бібліотеки» немає; конкретну сторінку DRDC **не перевірено** (404 на передбачуваних URL).

---

## Додатково (неофіційні, але корисні — не рахуються як «сховища»)
- **archive.org** — масові копії старих FM 5-xxx (мости, саперна справа) і канадських B-GL; якість/актуальність не гарантована, **не перевірено** поштучно.
- **globalsecurity.org / fas.org/irp/doddir/army** — дзеркала старих FM/ATP; зручні, але без гарантії чинності.

## Практичні висновки
1. **Ядро бази:** APD (armypubs) — усі 3-34; USACE Publications — будівельна інженерія; MCPEL — USMC-специфіка; e-Publishing — AF CE; UFC/UFGS — нормативи проєктування.
2. **Доступність з не-.mil мережі:** armypubs, e-publishing, afcec, wbdg — тільки через релей/браузер; call.army.mil — недоступний узагалі (переїхав на army.mil/tllm-army); apps.dtic.mil — на maintenance (13.11.2026).
3. **Дублювання:** ATP 3-34.x ≡ MCRP/MCTP (спільні документи) — у базі тримати один запис із двома номерами.
4. **CUI-зона:** ATP 3-90.4 (mobility), частина CALL, TDM-PUBS USMC, CAR — потребують CAC/логіна; не обходити.
