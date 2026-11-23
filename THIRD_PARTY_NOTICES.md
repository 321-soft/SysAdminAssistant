# Third-party components / Сторонние компоненты

SysAdmin Assistant uses separately licensed components. Its user agreement does not replace their licenses. Preserve the notices shipped with each package and any bundled runtime installer.

| Component | Purpose | License / notices |
|---|---|---|
| .NET, ASP.NET Core, WPF, Windows Forms and Microsoft libraries | Application framework, database access and Windows integration | Microsoft/.NET distribution notices; NuGet package metadata |
| Entity Framework Core, Microsoft.Data.Sqlite, Microsoft.Data.SqlClient and dependencies | SQLite and SQL Server | Respective NuGet package licenses and notices |
| MailKit, MimeKit | IMAP and message parsing | MIT; package license files |
| PdfPig | PDF text extraction | Apache-2.0; Server/InvoiceLicenses/PdfPig-LICENSE.txt and PdfPig-NOTICES.txt |
| Tesseract .NET wrapper | Local OCR | Server/InvoiceLicenses/Tesseract-wrapper-LICENSE.txt |
| Tesseract native engine and tessdata_fast models | Local English/Russian OCR | Apache-2.0; Server/tessdata/LICENSE and NOTICE.md; native package notices |
| Leptonica | OCR image processing | Server/InvoiceLicenses/Leptonica-LICENSE.txt |
| Microsoft VC++ and .NET runtime installers, if bundled | Runtime prerequisites | Microsoft installer license and distribution terms; not part of this project's source license |

Exact versions are declared in project files and resolved dependency assets during restore. Publication preserves third-party license files and OCR model notices. Equipment illustrations supplied by this project are original illustrations; product names and trademarks belong to their respective owners.

Сторонние лицензии сохраняются в опубликованном комплекте. Удаление лишних служебных документов не означает удаление обязательных уведомлений правообладателей. Актуальные версии зависимостей указаны в проектах; состав транзитивных компонентов определяется восстановлением NuGet.
