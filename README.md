# SmartWare AI

SmartWare AI is an ASP.NET Core MVC warehouse management system targeting .NET 10.

## Architecture

- `src/SmartWare.Web`: MVC presentation and application startup.
- `src/SmartWare.Application`: use cases, service contracts, DTOs, and validation.
- `src/SmartWare.Domain`: entities, enums, constants, and business rules.
- `src/SmartWare.Infrastructure`: EF Core, SQL Server, Identity persistence, and external integrations.
- `tests`: unit, integration, and authorization test projects.
- `docs`: analysis and design documents.

## Implemented modules

- SQL Server database model and initial EF Core migration.
- ASP.NET Core Identity authentication with `Admin`, `Manager`, and `Employee` roles.
- Administrator user management with audit logging.
- Responsive role-aware application layout.
- Live dashboard metrics, low-stock alerts, recent receipts, and six-month stock movement chart.
- Category, supplier, and product management with search, filtering, pagination, audit logging,
  safe deactivation of referenced records, and Excel-compatible CSV export.
- Secure product image upload on create/edit, with live preview, replacement/removal, generated
  file names, JPEG/PNG/WebP signature validation, and a 5 MB size limit.
- Import receipt workflow with role-separated creation, approval/rejection, atomic completion,
  inventory transactions, and moving-average cost calculation.
- Export receipt workflow with optional order linking, approval-time stock reservation,
  atomic stock release/issue, frozen outbound cost, and `OUT` inventory transactions.
- Inventory overview with current/reserved/available quantities, stock-level alerts,
  slow-moving indicators, valuation, and searchable `IN`/`OUT` transaction history.
- Customer management with operational statistics, search, status filtering, audit logging,
  safe deactivation of referenced customers, and Excel-compatible CSV export.
- Sales order workflow with validated customer/product lines, controlled status transitions,
  optional links to export receipts, audit logging, and Excel-compatible CSV export.
- Role-protected warehouse reports with period filters, import/export/inventory KPIs,
  revenue, COGS, gross profit, product/supplier/customer analysis, CSV/print export,
  and a transparent SMA demand forecast with replenishment risk indicators.
- Role-aware Google Gemini warehouse chatbot for product/stock lookup, low-stock alerts,
  import/export analysis, supplier questions, and authorized warehouse reports.
- Hybrid RAG over warehouse procedures and policies. Documents are split into chunks, embedded
  with Gemini, stored in SQL Server, retrieved by semantic similarity, and filtered by role before
  any context is sent to Gemini. Structured warehouse figures still come from bounded read-only SQL.
- Per-user and per-role chat history with multi-turn context, session listing/loading, new-chat,
  and session deletion. A user can never load or delete another user's conversation, and a role
  change cannot expose a conversation created under a different permission level.
- Admin/Manager knowledge management at `/kho-tri-thuc`, supporting pasted text and `.txt`, `.md`,
  or `.csv` files, role assignment, activation/deactivation, and embedding re-indexing.

## Gemini chatbot configuration

The API key is intentionally absent from configuration files. Configure it with User Secrets:

```powershell
dotnet user-secrets set "Gemini:ApiKey" "<your-gemini-api-key>" `
  --project src/SmartWare.Web
```

Alternatively, set the standard environment variable `GEMINI_API_KEY` or the ASP.NET Core
configuration variable `Gemini__ApiKey`. The default generation model is `gemini-3.6-flash` and
the default embedding model is `gemini-embedding-2` with 768 dimensions. These can be overridden
through `Gemini:Model`, `Gemini:EmbeddingModel`, `Gemini:EmbeddingDimensions`, `Gemini:RagTopK`,
`Gemini:RagMinimumScore`, `Gemini:MaxOutputTokens`, and `Gemini:ThinkingLevel` (or their
double-underscore environment-variable equivalents). The warehouse assistant defaults to
`MINIMAL` thinking and a 4,096-token response budget to avoid spending the response allowance on
hidden reasoning. If Gemini still returns `MAX_TOKENS`, the backend retries once with a larger cap.

Gemini never receives database credentials and cannot execute SQL. It receives only the bounded
context selected by backend queries after role checks. The chatbot has no write, delete, approval,
or permission-management operations.

The knowledge base is also role-scoped. Retrieval candidates are filtered by the current user's
role before semantic scoring, and only the top matching chunks are included in the prompt.
RAG is invoked only for questions containing a procedure, policy, audit, approval, or handling
intent; ordinary stock/report questions skip the embedding request and use the bounded SQL context.

## Database

The development configuration uses SQL Server LocalDB and database `SmartWareDB`.
EF Core tools are installed as a repository-local .NET tool.

```powershell
dotnet tool restore
dotnet tool run dotnet-ef database update `
  --project src/SmartWare.Infrastructure/SmartWare.Infrastructure.csproj `
  --startup-project src/SmartWare.Web/SmartWare.Web.csproj
```

Use environment variables or deployment secrets to override `ConnectionStrings__DefaultConnection`
outside local development.

## Product images

Administrators can upload an image while creating or editing a product. Uploaded files are stored
under `src/SmartWare.Web/wwwroot/uploads/products` at runtime, while the relative URL continues to
use the existing `Product.ImageUrl` field, so no additional database migration is required. Runtime
uploads are excluded from Git; production deployments should persist or mount this directory if
images must survive a redeployment.

## Development administrator

The application always seeds the `Admin`, `Manager`, and `Employee` roles. A development
administrator is created only when its email and password are supplied through User Secrets:

```powershell
dotnet user-secrets set "DevelopmentAdmin:Email" "admin@smartware.local" `
  --project src/SmartWare.Web
dotnet user-secrets set "DevelopmentAdmin:Password" "<your-development-password>" `
  --project src/SmartWare.Web
```

Do not put the administrator password in `appsettings.json` or commit it to Git.

## Development sample data

When the application starts in the `Development` environment, it seeds an idempotent demo data
set after the development administrator is available. The data uses `DEMO-` codes and includes
products at out-of-stock, low, healthy, and excess levels; suppliers; customers; six months of
completed imports, exports, and sales; plus pending/approved documents for workflow testing.
It also creates four sample warehouse-procedure documents and their embeddings when Gemini is
configured, so free-form RAG questions can be tested immediately.

Set `DevelopmentSeedData:Enabled` to `false` in local configuration if sample data is not wanted.
The seeder never runs outside the `Development` environment and skips execution when demo products
already exist.

## Build and test

```powershell
dotnet build SmartWare.slnx
dotnet test SmartWare.slnx --no-build
```
