# Сховища військово-інженерних документів: довідник для масового вивантаження

Дата перевірки: 2026-11-13. Усе нижче перевірено живими запитами (`curl` / `web_extract`), крім позицій, явно позначених **«не перевірено»**.
Позначки доступу: `REST` — документований JSON API; `OAI` — OAI-PMH; `SITEMAP` — sitemap/лістинг; `HTML` — тільки веб-сторінки; `STATIC` — прямі PDF/DjVu за відомим шаблоном URL.

## Зведена таблиця

| # | Сховище | Хост | Машинний інтерфейс | Масове вивантаження | Обсяг (перевірено) | Мова | Доступ |
|---|---------|------|--------------------|---------------------|--------------------|------|--------|
| 1 | DTIC / DTIC Discover | discover.dtic.mil, apps.dtic.mil | немає публічного API/OAI; пошук = Google CSE | ні (краулінг PDF за шаблоном, зараз недоступно) | ~17 700 результатів за «military engineering» | en | частково, зараз offline |
| 2 | Internet Archive | archive.org | REST (advancedsearch, scrape, metadata), **OAI немає** | так | «technical manual» 6 364; «field manual» 898; «military engineering» 2 236 | en (багато) | вільний |
| 3 | HathiTrust | hathitrust.org | REST (Bibliographic API, Data API), HTML full-text | тільки через інституцію (Data API / datasets) | ~18 млн томів (заявлено) | en + ін. | обмежений |
| 4 | Google Books | books.google.com, googleapis.com/books | REST (офіційний) | ні; PDF лише public domain | ~40 млн (заявлено) | багато | обмежений квотою |
| 5 | CORE | core.ac.uk | REST v3 | так | **390 558 801** записів | багато | вільний (ключ для лімітів) |
| 6 | BASE | base-search.net | REST (потрібна реєстрація IP) | так, після whitelist | ~400 млн (заявлено) | багато | обмежений реєстрацією |
| 7 | OAPEN | library.oapen.org | OAI-PMH + HTML | так | **59 143** записи | en/nl + ін. | вільний |
| 8 | OpenAIRE | api.openaire.eu | REST (search + Graph v12) | так (метадані) | «military engineering» 1 252 | багато | вільний |
| 9 | LoC | loc.gov | REST JSON (`fo=json`), Z39.50/SRU | так (після доступу) | не перевірено | en | вільний, але Cloudflare |
| 10 | BnF (каталог + Gallica) | catalogue.bnf.fr, gallica.bnf.fr | SRU (каталог), SRU/OAI/IIIF (Gallica) | так (Gallica) | **2 705** за «génie militaire» | fr | вільний |
| 11 | DNB | services.dnb.de, dnb.de | OAI-PMH (з сетами) | так | не перевірено (великий) | de | вільний |
| 12 | Biblioteka Narodowa (PL) | data.bn.org.pl, polona.pl | REST (bibs/authorities), дампи | так (дампи БД) | не перевірено | pl | вільний |
| 13 | НРАТ (Україна) | nrat.ukrintei.ua | немає API/OAI; HTML | ні (ліміт 1000 на видачу) | **796 016** документів | uk | вільний |
| 14 | ДНТБ України | dntb.gov.ua | немає (WordPress) | ні | — | uk | вільний |
| 15 | НУОУ | nuou.org.ua | немає репозиторію | ні | — | uk | частково |
| 16 | НБУВ (Ірбіс) | irbis-nbuv.gov.ua | Z39.50/Ірбіс, HTML | ні | не перевірено | uk | блокує ботів |
| 17 | КПІ / Львівська політехніка | ela.kpi.ua, ena.lpnu.ua | DSpace 7: OAI + REST | так | не перевірено | uk/en | вільний |
| 18 | Wikisource | *.wikisource.org | MediaWiki API | так | не перевірено | багато | вільний |

---

## 1. DTIC (Defense Technical Information Center)

- **Адреси:** https://discover.dtic.mil/ (пошук), https://apps.dtic.mil/ (репозиторій PDF)
- **Тип доступу:** HTML; пошук на `discover.dtic.mil/results/?q=` віддає результати **через Google Programmable Search** (`#gsc.tab=0`). Публічного REST/OAI немає — сторонній каталог apis.io прямо фіксує: «Programmatic access is not publicly documented».
- **Стан на 2026-11-13 (критично):**
  - на сторінці результатів банер: **«DTIC Public Search is offline while we enhance the capability»**;
  - `apps.dtic.mil` цілком віддає `landingpage/maint.html` («Our Site is Getting an Upgrade»), будь-який `…/sti/tr/pdf/ADAxxxxxx.pdf` → редирект на maintenance, HTTP 403;
  - отже масове вивантаження **зараз фізично неможливе**.
- **Структура пошуку:** 5 табів — `All results`, `DoD Technical Reports – ALL`, `This Site`, `DoD TR PDF Documents`, `NDIA Conference Proceedings`, `DoD TR Citations`. Фасети/сортування — на боці CSE.
- **Шаблон статичних PDF (для майбутнього):** `https://apps.dtic.mil/sti/tr/pdf/{ACCESSION}.pdf`, напр. `ADA540625`, `ADA592582`; цитати — `/sti/citations/{ACCESSION}`.
- **Обсяг:** ~17 700 результатів за «military engineering» (CSE, орієнтовно). Повний фонд DTIC — мільйони, але публічно лише unclassified/unlimited.
- **Мова:** en. **Доступ:** безкоштовно; частина фонду — лише через R&E Gateway з CAC/PIV.
- **Примітка:** єдиний реальний шлях до масиву — дочекатися відновлення `apps.dtic.mil` і краулити за шаблоном accession-номерів (ADxxxxxxx), бо API немає. Під час простою альтернатива — дзеркала в IA (див. §2) і NTIS.

## 2. Internet Archive

- **Адреси:** https://archive.org/ ; https://archive.org/advancedsearch.php ; https://archive.org/services/search/v1/scrape
- **Тип доступу:** `REST` — три робочі кінцеві точки, **OAI-PMH відсутній** (перевірено: `/oai/2.0/`, `/oai/2.0/oai.php`, `/oai`, `/services/oai` → усі 404).
- **Робочі виклики (перевірено):**
  - `https://archive.org/advancedsearch.php?q="military engineering" AND mediatype:texts&fl[]=identifier&fl[]=title&rows=100&page=1&output=json`
  - `https://archive.org/services/search/v1/scrape?q=collection:(field-technical-manuals)&fields=identifier,title&count=100` — **count мінімум 100**, інакше 400; курсор `next` для пагінації.
  - `https://archive.org/metadata/{identifier}` — список файлів (`_djvu.txt`, `_text.pdf`, `_bw.pdf`, `jp2.zip`).
  - Завантаження: `https://archive.org/download/{identifier}/{file}`.
- **Структура колекцій (перевірені лічильники):** `field-technical-manuals` — 2 031; `folkscanomy_defense` — 3 399; `manual_packs` — 264; `manuals` та `manuals_contributions` — сотні тисяч (рахунок роздутий через вкладені колекції, точне число недостовірне); `usgovernmentmirrors`, `government-documents`, `nationalsecurityarchive` — мільйони.
- **Запити за темами:** `subject:("military engineering")` → 2 068; `"technical manual" AND mediatype:texts` → 6 364; `title:("field manual") AND mediatype:texts` → 898.
- **Приклади:** `MilitaryFieldManuals1900-1913` (422 файли), `MManuals` (6 789 файлів), `military-field-manuals-and-guides` (2 084 файли).
- **Мова:** переважно en, багато інших. **Доступ:** вільний; частина «borrow-only» (DRM) — для PDF не придатна.
- **Примітка:** найкраще працює зв'язка «scrape для переліку ідентифікаторів → metadata → download». `_djvu.txt` дає готовий OCR.

## 3. HathiTrust

- **Адреси:** https://catalog.hathitrust.org/ ; https://babel.hathitrust.org/cgi/ls (full-text search); https://babel.hathitrust.org/cgi/htd/ (Data API)
- **Тип доступу:** `REST` (Bibliographic API, Data API), `HTML` (full-text search із фасетами).
- **Перевірено:** `https://catalog.hathitrust.org/api/volumes/brief/json/oclc:12345` → 200, JSON `{records, items}`. Full-text пошук «military engineering» віддає результати (напр. «Military engineering. General Staff, War Office, pt. 2 (1910)», «Manual of road construction and maintenance, compiled at the School of Military Engineering»).
- **Data API:** `https://babel.hathitrust.org/cgi/htd/…` → **403 без OAuth** — доступ лише для установ-членів; для масових даних є окрема програма «research datasets» (public domain, за заявкою).
- **Обсяг:** ~18 млн оцифрованих томів (заявлено HathiTrust, не перевірено запитом).
- **Мова:** en + багато. **Доступ:** публічний лише full-view (public domain, до 1929) — поштучно; масово — тільки через інституцію.
- **Примітка:** для військово-інженерної історії (British War Office «Military Engineering», 1910-1914) — найкраще джерело, але без масового API. Search-only томи не дають PDF.

## 4. Google Books

- **Адреси:** https://www.googleapis.com/books/v1/volumes ; https://books.google.com/
- **Тип доступу:** `REST` (офіційний, ключ обов'язковий для стабільності).
- **Перевірено:** без ключа → **HTTP 429**, «Quota exceeded … Queries per day … project_number:624717413613». Тобто спільна квота вичерпується; потрібен власний ключ (1000 запитів/добу).
- **Обсяг:** ~40 млн томів (заявлено). **Мова:** багато. **Доступ:** метадані — вільно; повний PDF — лише public domain і лише поштучно (`books.google.com/books/download/…`).
- **Примітка:** для масового вивантаження непридатне; корисно для ідентифікації видань і пошуку всередині книги.

## 5. CORE

- **Адреса:** https://api.core.ac.uk/v3/search/works/ ; портал https://core.ac.uk/
- **Тип доступу:** `REST`. **Важливо:** без скісної риски в кінці — 301; правильно `…/works/?q=…`.
- **Перевірено:** `https://api.core.ac.uk/v3/search/works/?q=*&limit=1` → `{"totalHits":390558801}`. Запит `q="military engineering"` без ключа віддає результати (обмежений рейт-ліміт); для регулярної роботи — безкоштовний API-ключ (реєстрація).
- **Структура:** поля `title, authors, abstract, downloadUrl, dataProviders, documentType, doi, yearPublished`; фільтри `q=`, `limit`, `offset`; окремі endpoints `/v3/outputs/{id}`, `/v3/data-providers/{id}`.
- **Обсяг:** ~390,6 млн записів. **Мова:** усі. **Доступ:** вільний (ключ для лімітів).
- **Приклад:** запит вище повернув «Regulations for the Corps of Cadets of the Virginia Military Institute» з `dataProviders: VMI Archives`.
- **Примітка:** метадані + посилання на PDF у сховищі-джерелі; самого файлу CORE не завжди тримає.

## 6. BASE (Bielefeld Academic Search Engine)

- **Адреси:** https://www.base-search.net/ ; API `https://api.base-search.net/cgi-bin/BaseHttpSearchInterface.fcgi`
- **Тип доступу:** `REST`, але **прив'язаний до IP**.
- **Перевірено:** `…?func=PerformSearch&query=dctitle:military&format=json` → 200, але тіло: `{"error": "Access denied for IP address 31.128.168.144 and user agent curl/8.18.0."}`. Отже потрібна реєстрація IP у Bielefeld (безкоштовна для науки).
- **Обсяг:** ~400 млн записів (заявлено). **Мова:** багато. **Доступ:** безкоштовно після whitelist.
- **Примітка:** агрегатор метаданих репозиторіїв; цінний для пошуку «де лежить PDF», не для самого файлу.

## 7. OAPEN

- **Адреси:** https://library.oapen.org/ ; OAI `https://library.oapen.org/oai/request`
- **Тип доступу:** `OAI-PMH` (перевірено `verb=Identify` → «OAPEN Library») + `HTML`.
- **Перевірено:** `?verb=ListRecords&metadataPrefix=oai_dc` → `completeListSize="59143"`.
- **Структура:** стандартні `ListSets/ListRecords/GetRecord`, `metadataPrefix=oai_dc` (є й інші формати). Веб-пошук `library.oapen.org/discover?query=` віддає 403 для ботів — користуйтесь OAI.
- **Обсяг:** **59 143** записи. **Мова:** en/nl та ін. **Доступ:** вільний, переважно CC-BY.
- **Примітка:** військово-інженерна тематика тонка (це переважно гуманітаристика/соцнауки), але OAI бездоганний і повністю вивантажуваний.

## 8. OpenAIRE

- **Адреси:** https://api.openaire.eu/search/publications ; Graph: `https://api.openaire.eu/graph/v1/researchProducts`; документація https://graph.openaire.eu/docs/
- **Тип доступу:** `REST` (два покоління: старий `search/` і Graph v1).
- **Перевірено:** `…/search/publications?title=military+engineering&format=json&size=1` → `total: 1252`. Graph: параметр **`pageSize`**, не `size` (інакше 400 «Unknown parameter: size»).
- **Структура:** `format=json|xml`, `size`, `page`, `keywords`, `fromDateAccepted`; Graph — `search`, `pageSize`, `type=publication`, курсор.
- **Обсяг:** метадані з Crossref, DOAJ, Unpaywall, DataCite, репозиторіїв OpenDOAR. **Мова:** усі. **Доступ:** вільний.
- **Примітка:** тільки метадані + посилання; корисний як «індекс усіх репозиторіїв» для подальшого dobування PDF.

## 9. Library of Congress

- **Адреси:** https://www.loc.gov/ ; JSON `https://www.loc.gov/search/?q=…&fo=json`
- **Тип доступу:** `REST` JSON (`fo=json`), Z39.50/SRU для каталогу.
- **Перевірено:** **403** — Cloudflare-перевірка («Триває перевірка безпеки», Ray ID) і для curl, і для реального браузера з нашого IP. Тобто наявність API **не підтверджена наживо**, лише за документацією — **«не перевірено»**.
- **Структура:** `loc.gov/{format}/?q=&fo=json&c=&sp=`; колекції `loc.gov/collections/{slug}/?fo=json`.
- **Обсяг:** десятки мільйонів одиниць. **Мова:** en + ін. **Доступ:** вільний (але антибот).
- **Примітка:** для військово-інженерних настанов LoC цінний колекціями `Military Legal Resources` і загальним книжковим фондом; потрібен IP/браузер, який проходить Cloudflare.

## 10. BnF: каталог і Gallica

- **Адреси:** https://catalogue.bnf.fr/api/SRU ; https://gallica.bnf.fr/ (SRU, OAI, IIIF)
- **Тип доступу:** `SRU` (каталог — перевірено), `SRU/OAI/IIIF` (Gallica — заявлено).
- **Перевірено (каталог):** `https://catalogue.bnf.fr/api/SRU?version=1.2&operation=searchRetrieve&query=bib.anywhere all "genie militaire"&maximumRecords=1` → `numberOfRecords: 2705`, формат `marcxchange`.
- **Не перевірено:** Gallica `https://gallica.bnf.fr/SRU` і `https://gallica.bnf.fr/OAI` → **403** з нашого IP (антибот). За документацією Gallica віддає SRU, OAI-PMH і IIIF-маніфести, плюс пряме завантаження PDF (`/ark:/12148/{ark}.pdf`).
- **Обсяг:** каталог BnF — мільйони; Gallica — ~10 млн документів (заявлено). **Мова:** fr. **Доступ:** вільний.
- **Примітка:** для французьких інженерних настанов (génie) — головне джерело; PDF у Gallica завантажується цілком, на відміну від HathiTrust.

## 11. DNB (Deutsche Nationalbibliothek)

- **Адреса:** https://services.dnb.de/oai/repository
- **Тип доступу:** `OAI-PMH`.
- **Перевірено:** `verb=Identify` → «OAI-Repository of the German National Library V2.0.11»; `verb=ListSets` віддає сетами за предметними групами DNB (`dnb`, `dnb:sg0`, `dnb:sg000`, `dnb:sg004` …).
- **Структура:** `ListRecords&metadataPrefix=oai_dc` (+ marcxml), фільтр за сетом предметної групи. SRU для каталогу DNB — **не перевірено**.
- **Обсяг:** не перевірено запитом; національна бібліографія — мільйони. **Мова:** de. **Доступ:** вільний (метадані).
- **Примітка:** PDF за OAI не віддаються — це метадані; повні тексти — через DNB або локальні репозиторії.

## 12. Biblioteka Narodowa (Польща) і Polona

- **Адреси:** https://data.bn.org.pl/ (API + дампи), https://polona.pl/ (цифрова бібліотека)
- **Тип доступу:** `REST` (bibs, authorities, MeSH-Pol) + **дампи баз** (`data.bn.org.pl/databases`); Polona — `HTML` (+ IIIF).
- **Перевірено:** `https://data.bn.org.pl/api/institutions/bibs.json?limit=1` → 200; сторінка описує API «Rekordy bibliograficzne», «Rekordy wzorcowe», «Bazy do pobrania»; `https://polona.pl/` → 200; `polona.pl/api/pdn-catalogue/api-docs` → OpenAPI, але це **каталог-синхронізація** (наукові дисципліни, установи, ключові слова), а не пошук об'єктів.
- **Не перевірено:** публічний пошуковий API самої Polona (спроба `polona.pl/api/entities/search` → 404). Завантаження PDF — поштучно зі сторінки об'єкта — **«не перевірено»**.
- **Обсяг:** не перевірено. **Мова:** pl. **Доступ:** вільний.
- **Примітка:** для польських військово-інженерних видань (saperzy, fortyfikacje) Polona дає повні скани; масово — тільки дампи каталогу, не файли.

## 13. НРАТ — Національний репозитарій академічних текстів (Україна)

- **Адреса:** https://nrat.ukrintei.ua/ , пошук https://nrat.ukrintei.ua/ua/searchdb/
- **Тип доступу:** `HTML` лише. `https://nrat.ukrintei.ua/api/` → 404; `https://nrat.ukrintei.ua/oai/request?verb=Identify` → з'єднання не встановлюється. **OAI/API немає.**
- **Перевірено:** сторінка пошуку → **«796016 documents found. Search results are limited. 1000 documents displayed»**.
- **Структура:** типи академічних текстів — `reports` (звіти НДР), `dissertations`, `articles`, `search.vnz` (локальні репозиторії). Є розширений пошук і ШІ-помічник; кожен запис лінкує на локальний репозиторій (напр. `dspace.znu.edu.ua`).
- **Обсяг:** **796 016** документів. **Мова:** uk (інтерфейс uk/en). **Доступ:** вільний, потрібна реєстрація для частини функцій.
- **Примітка:** для військово-інженерних звітів — єдиний національний агрегатор, але без машинного інтерфейсу й з жорстким лімітом 1000 результатів на запит; доведеться нарізати вибірку за роками/установами.

## 14. ДНТБ України (State Scientific and Technical Library)

- **Адреса:** https://dntb.gov.ua/ (uk/en)
- **Тип доступу:** `HTML` (WordPress). Репозиторію й OAI не виявлено.
- **Перевірено:** головна 200; зміст — новини, конференції Open Science, доступ до Scopus/WoS.
- **Обсяг:** не перевірено. **Мова:** uk/en. **Доступ:** вільний сайт, фонди — за читацьким квитком.
- **Примітка:** як джерело масових PDF не підходить; корисна як точка входу до українських науково-технічних баз.

## 15. НУОУ — Національний університет оборони України

- **Адреса:** https://nuou.org.ua/ ; бібліотека https://nuou.org.ua/u/stru/others/biblioteka/
- **Тип доступу:** `HTML`. **Репозиторію немає** — перевірено: на сторінці бібліотеки немає посилань на DSpace/EPrints/Ірбіс; `/repozytarij/` → 404.
- **Що є:** розділ `/nauka/npub/` (публікації університету, монографії, «Біла книга АТО»), `/nauka/dostup-nat-ele-nau-inf-sys.html`.
- **Обсяг:** не перевірено. **Мова:** uk. **Доступ:** частковий (частина матеріалів лише для внутрішніх).
- **Примітка:** масового вивантаження немає; окремі PDF — прямі лінки на `nuou.org.ua/assets/documents/*.pdf`.

## 16. НБУВ — Національна бібліотека України ім. Вернадського

- **Адреси:** https://irbis-nbuv.gov.ua/ (Ірбіс), http://www.irbis-nbuv.gov.ua/cgi-bin/irbis_nbuv/cgiirbis_64.exe
- **Тип доступу:** `HTML` + Ірбіс/Z39.50 (заявлено), `SITEMAP` немає.
- **Перевірено:** `http://irbis-nbuv.gov.ua/` → **403** (антибот), `cgiirbis_64.exe` → 301. Наживо не підтверджено — **«не перевірено»**.
- **Обсяг:** мільйони бібліографічних записів (заявлено). **Мова:** uk/ru. **Доступ:** вільний пошук, повні тексти — обмежено.
- **Примітка:** українська військово-технічна періодика радянського періоду часто є тільки тут, у паперовому вигляді; масова викачка малоймовірна.

## 17. Університетські репозиторії з інженерними програмами (DSpace 7)

- **КПІ ім. Ігоря Сікорського:** https://ela.kpi.ua/ — DSpace 7, 38 фондів.
  - `OAI`: `https://ela.kpi.ua/server/oai/request?verb=Identify` → **200**;
  - `REST`: `https://ela.kpi.ua/server/api` → **200**; також OpenSearch `https://ela.kpi.ua/server/opensearch/search?format=atom&query=*`.
- **Львівська політехніка:** https://ena.lpnu.ua/ — DSpace 7; `…/server/oai/request?verb=Identify` → **200** (старий шлях `/oai/request` → 404).
- **Обсяг:** не перевірено (десятки тисяч). **Мова:** uk/en. **Доступ:** вільний.
- **Примітка:** це шаблон для всіх українських DSpace 7: перелік через OAI `ListRecords`, файли — через REST `…/server/api/core/bitstreams/{uuid}/content`. Для військово-інженерних тем — слабко, але для суміжних (будівництво, механіка, РЕБ) корисно.

## 18. Wikisource / мілітарні вікі-архіви

- **Адреси:** https://en.wikisource.org/ , https://uk.wikisource.org/
- **Тип доступу:** `REST` — MediaWiki API.
- **Перевірено:** `https://en.wikisource.org/w/api.php?action=query&list=search&srsearch=military engineering&format=json` → 200.
- **Структура:** `action=query&list=search`, `prop=extracts`, категорії `Category:Military engineering`; сторінки мають `action=raw` і PDF-експорт (`/w/index.php?title=X&action=render`), файли — на Commons.
- **Обсяг:** не перевірено. **Мова:** багато. **Доступ:** вільний (CC).
- **Примітка:** невеликий корпус, але добре структурований і повністю машинно доступний; корисно для статутів/настанов у текстовому вигляді.

---

## Що перевірено відмовило (не витрачати час)

| Кінцева точка | Результат |
|---|---|
| `apps.dtic.mil/*` (усі PDF і `/oai/oai`) | 403 + maintenance-сторінка |
| `archive.org/oai/2.0/`, `/oai/2.0/oai.php`, `/oai`, `/services/oai` | 404 — OAI у IA немає |
| `api.core.ac.uk/v3/search/works` (без скісної риски) | 301 |
| `api.openaire.eu/graph/v1/researchProducts?...&size=` | 400 «Unknown parameter: size» → треба `pageSize` |
| `api.base-search.net/...` | «Access denied for IP … curl» → реєстрація IP |
| `googleapis.com/books/v1/volumes` без ключа | 429, квота проєкту вичерпана |
| `www.loc.gov/...?fo=json` | 403 Cloudflare (і curl, і браузер) |
| `gallica.bnf.fr/SRU`, `/OAI` | 403 |
| `nrat.ukrintei.ua/api/`, `/oai/request` | 404 / немає з'єднання |
| `nuou.org.ua/repozytarij/` | 404 — репозиторію немає |
| `ena.lpnu.ua/oai/request` | 404 (робочий — `/server/oai/request`) |

## Робочі рецепти (перевірені)

```bash
# Internet Archive: перелік → файли → завантаження
curl -s 'https://archive.org/services/search/v1/scrape?q=collection:(field-technical-manuals)&fields=identifier,title&count=100'
curl -s 'https://archive.org/metadata/MManuals' | jq -r '.files[].name' | grep -E '_djvu.txt$|_text.pdf$'
curl -sLO 'https://archive.org/download/MManuals/<file>'

# CORE
curl -s 'https://api.core.ac.uk/v3/search/works/?q="military engineering"&limit=100&offset=0'

# OAPEN (повний дамп)
curl -s 'https://library.oapen.org/oai/request?verb=ListRecords&metadataPrefix=oai_dc'

# BnF каталог
curl -s 'https://catalogue.bnf.fr/api/SRU?version=1.2&operation=searchRetrieve&query=bib.anywhere all "genie militaire"&maximumRecords=50'

# DNB
curl -s 'https://services.dnb.de/oai/repository?verb=ListRecords&metadataPrefix=oai_dc&set=dnb:sg'

# Український DSpace 7
curl -s 'https://ela.kpi.ua/server/oai/request?verb=ListIdentifiers&metadataPrefix=oai_dc'
```

## Пріоритет під задачу «масово витягнути військово-інженерні документи»

1. **Internet Archive** — єдине велике сховище, яке зараз реально віддає масиви PDF/DjVu без реєстрації й без API-ключа.
2. **CORE** — 390 млн метаданих, пошук працює одразу; далі — за `downloadUrl`.
3. **Gallica / BnF** — повні PDF французьких настанов (потрібен IP, що проходить антибот).
4. **DTIC** — чекати відновлення `apps.dtic.mil`; API не буде, тільки краулінг за accession.
5. **Українські** — тільки НРАТ (HTML, ліміт 1000) і університетські DSpace; НУОУ/ДНТБ/НБУВ масово не віддають.
