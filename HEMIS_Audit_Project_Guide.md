# HEMIS Audit System — Project Guide
### A plain-English explanation of everything you built

---

## 1. What Is This System?

You built a **web-based audit tool** for reviewing HEMIS data submitted by South African universities and colleges to the Department of Higher Education and Training (DHET).

HEMIS stands for **Higher Education Management Information System**. Every year, institutions must submit student, course, and qualification data to DHET. Before that data is submitted, it needs to be checked for errors — missing values, invalid dates, duplicates, mismatches between tables, and so on.

Our system — **HEMIS Audit** — is the tool that performs those checks automatically. A data analyst opens the system, connects it to an institution's HEMIS database, runs a set of validation rules, reviews the results, and signs off before export.

---

## 2. Who Uses It and How?

There are four types of users:

| Role | What they do |
|---|---|
| **Admin** | Manages the system — creates clients, creates users, assigns users to clients, views audit logs |
| **Data Analyst** | Connects to HEMIS databases, runs validations, reviews results, saves workspaces, signs off |
| **Manager** | Reviews the data analyst's signed results and adds their own signoff |
| **Director** | Final level of review — adds the director signoff after manager has signed |
| **Trainee** | Can view results and download files, but cannot run validations or sign off |

The typical flow for a validation job:

1. Admin creates a **Client** (an institution like TUT or UNISA) with a fiscal year
2. Admin assigns users to that client with their roles
3. Data Analyst logs in, goes to the client's dashboard, picks a rule
4. Data Analyst connects to the HEMIS SQL Server database, configures the tables and columns
5. They click "Run Validation" — the system queries the HEMIS data and checks it
6. Results appear on screen: PASS rows, FAIL rows, statistics, and exception categories
7. Data Analyst saves the workspace and adds their signoff
8. Manager logs in and adds their signoff
9. Director adds final signoff
10. Anyone with access can download Excel, CSV, or SQL exports

---

## 3. The Big Picture — How the System Is Structured

Your application follows a pattern called **MVC (Model-View-Controller)**. Think of it as a restaurant:

- **Models** = the recipes and ingredients (the data)
- **Controllers** = the waiters (they take requests and pass them to the kitchen)
- **Views** = the plates (what the user actually sees on screen)
- **Services** = the kitchen (where the real work happens)

There are actually **two separate databases** in this system:

| Database | What it stores | Technology |
|---|---|---|
| **Application Database** | Users, clients, assignments, audit logs | SQLite (a small local file) |
| **HEMIS Database** | The institution's actual data (students, courses, qualifications) | SQL Server (the institution's own server) |

The application database is yours — it lives with the application. The HEMIS database belongs to the institution and your system connects to it remotely when a validation is run.

---

## 4. The Entry Point — `Program.cs`

**File:** `HemisAudit/Program.cs`

This is the very first file that runs when the application starts. Think of it as the building manager who sets up everything before anyone arrives.

It does the following things in order:

### 4.1 Sets up the application database
Connects to a local SQLite file for storing users, clients, and audit logs.

### 4.2 Sets up Identity (login and user management)
This is ASP.NET Identity — a ready-made system for handling logins, passwords, and roles. It configures:
- Passwords must have uppercase, lowercase, a number, and a special character
- After 5 wrong login attempts, the account is locked for 15 minutes
- Each user must have a unique email address

### 4.3 Sets up the session cookie
When a user logs in, a cookie called `HemisAudit.Auth.v2` is created in their browser. It expires after 8 hours of inactivity.

### 4.4 Registers all services
Every service (worker class) is registered here so the system knows which class to use when a controller asks for help. This is called **Dependency Injection** — instead of creating objects manually, you declare what you need and the system creates it for you. Example:
```
builder.Services.AddScoped<IRule65Service, Rule65Service>();
```
This means: "When anyone asks for IRule65Service, give them Rule65Service."

### 4.5 Sets up compression
Responses are compressed using Brotli and Gzip before being sent to the browser, making the system faster.

### 4.6 Defines URL routes
Every URL in the system is mapped here. For example:
- `/Rule65` maps to `Rule65Controller → Index action`
- `/Rule65/Run/42` maps to `Rule65Controller → Run action` with id=42
- `/Dashboard` maps to `DashboardController → Index`

---

## 5. The Database Layer

### 5.1 Application Database Context — `Data/ApplicationDbContext.cs`

This file is the **gateway** between your C# code and the SQLite database. It defines four tables:

| Table | Purpose |
|---|---|
| `Clients` | One row per institution engagement (e.g. "TUT FY2024") |
| `ClientUsers` | Which users are assigned to which clients, and in what role |
| `ValidationRuns` | Records of each validation that has been run and saved |
| `AuditLogs` | A history of every significant action performed in the system |

### 5.2 Models — `Models/ApplicationModels.cs`

Models are C# classes that represent database rows. Each property becomes a column.

**`ApplicationUser`** — extends the built-in Identity user with extra fields:
- `FirstName`, `LastName`, `EmployeeCode`
- `IsActive` — whether the account is enabled
- `PasswordSetDate`, `PasswordChangedAt` — used by the password expiry policy
- `ProfilePicturePath` — for the profile photo

**`Client`** — represents one engagement:
- `Name` — the institution name
- `FiscalYear` — e.g. "FY2024"
- `Status` — Pending, Active, or Closed

**`ClientUser`** — the link between a user and a client:
- `EngagementRole` — DataAnalyst, Manager, Director, or Trainee
- Links `ApplicationUser` and `Client` together

**`ValidationRun`** — a saved record of a rule being run:
- Stores the connection details, table names, result counts, and the full results as JSON
- `IsCurrent` — only the most recent run per rule per client is marked as current

**`AuditLog`** — every important action logged:
- Who did it, when, from which IP address, and what they did

### 5.3 Database Bootstrapper — `Data/SystemDatabaseBootstrapper.cs`

Runs once on startup to make sure the **HEMIS system database** (the separate SQL Server database where validation results are stored) has all the required tables created. This database stores workspace state, review signoffs, run results, and other data that doesn't belong in the small SQLite file.

### 5.4 Database Seeder — `Data/DbInitializer.cs`

Also runs once on startup. It creates the default **Admin user** and the default **Admin role** if they don't already exist, so you can always log in to a fresh installation.

---

## 6. Controllers

Controllers are the **traffic directors**. They receive a request from the browser (e.g. "run Rule 65"), call the appropriate service to do the work, and return a response (either a page or a JSON result).

Every controller is decorated with `[Authorize]` — meaning you must be logged in. Some actions are also restricted by role.

### 6.1 `AccountController` — Login and Password Management

Handles everything related to authentication:

| Action | What it does |
|---|---|
| `Login` | Shows the login page; validates credentials; redirects on success |
| `Logout` | Signs the user out and clears the session |
| `ForgotPassword` | Sends a password reset email |
| `ResetPassword` | Processes the reset token and sets a new password |
| `ChangePassword` | Lets a logged-in user change their own password |
| `PasswordExpired` | Shown when the password policy age is exceeded |
| `RenewPassword` | Two-step password renewal flow for expired passwords |
| `AccessDenied` | Shown when a user tries to access something they don't have permission for |

### 6.2 `AdminController` — Managing the System

Only accessible to users with the Admin role. Manages clients and users.

| Action | What it does |
|---|---|
| `Clients` | Lists all client engagements |
| `CreateClient` | Creates a new client |
| `ClientDetail` | Shows a client's dashboard — all assigned users and all rule modules |
| `Users` | Lists all system users |
| `CreateUser` | Creates a new user account |
| `EditUser` | Changes a user's details or role |
| `AuditLog` | Shows the full history of all actions in the system |

### 6.3 `DashboardController`

The home screen after login. Shows all active clients the logged-in user is assigned to, and displays status cards for every validation rule.

### 6.4 `MessagesController`

An internal messaging system between users. Supports text messages and file attachments.

### 6.5 `ProfileController`

Lets a user update their own profile — name, department, office address, gender, and profile picture.

### 6.6 `Rule10Controller` — Rules 1 to 10 (Integrity Rules)

This single controller handles **Rules 1 through 10**, which are the basic HEMIS integrity checks. These rules check things like:
- Rule 1: Qualifications without a qualification type
- Rule 3: Duplicate qualification codes
- Rule 5: Invalid student numbers (placeholder 9999999 values)
- Rule 9: Course registrations for students who don't exist

These rules are simpler than the later rules and share one controller because they follow the same pattern.

### 6.7 Rule Controllers 11 to 65

From Rule 11 onwards, every rule has its own dedicated controller. Each controller follows an identical pattern:

| Action | What it does |
|---|---|
| `Index` (GET) | Loads the validation workspace page |
| `GetDatabases` | Called by the browser to list available SQL Server databases |
| `GetTables` | Lists tables in the selected database |
| `GetColumns` | Lists columns in the selected table |
| `VerifyTables` | Checks that the selected tables exist and counts their rows |
| `RunValidation` | Executes the validation query and returns results |
| `GetWorkspaceState` | Loads a previously saved workspace for the current client |
| `SaveWorkspace` | Saves the current configuration and results to the database |
| `BeginEdit` | Unlocks a saved workspace for re-running |
| `AddSignoff` / `RemoveSignoff` | Manages review signoffs |
| `DownloadExcel` | Returns an Excel file of the full results |
| `DownloadCsv` | Returns a CSV file of the full results |
| `DownloadSql` | Returns the SQL query that was used |
| `Run` (GET) | Shows a read-only view of a completed run (for sharing with reviewers) |

### 6.8 `ValidationOperationsController`

A background helper controller that handles long-running operations. When a validation takes a long time, the browser polls this controller for progress updates rather than waiting for the main request to finish. This is what drives the spinning progress indicator you see during validations.

---

## 7. Services

Services are the **workers** — they do the actual computing, database queries, and business logic. Controllers are thin; services are where the complexity lives.

### 7.1 The Interface Pattern

Every service has two files:
- `IRule65Service.cs` — the **interface** (a contract listing what the service can do)
- `Rule65Service.cs` — the **implementation** (the actual code)

Why? Because this makes the system easier to test and maintain. The controller only knows about the interface, not the implementation. If you ever want to change how the service works, you only change the implementation — nothing else needs to change.

### 7.2 Core System Services

**`SystemDatabaseService`** — All interactions with the HEMIS system SQL Server database (not the SQLite app database). Creates tables, retrieves workspace state, saves runs, manages signoffs. Every rule's service depends on this.

**`AuditLogService`** — Records events to the audit log. Called from controllers whenever something significant happens (login, run, download, etc.).

**`EmailService`** — Sends emails via SMTP. Used for password reset links.

**`PasswordPolicyService`** — Enforces the password age policy. Checks how old a user's password is and forces renewal when it expires.

**`PendingValidationCacheService`** — An in-memory store (singleton) that tracks long-running validations in progress. Used by the progress polling system so the browser can ask "is it done yet?"

**`ValidationOperationService`** — Works with the cache service to run validations in the background and stream progress back to the browser.

**`ExportService`** — Generates Excel and CSV exports for rules that use the shared export system (most rules 11–40).

**`ReviewSignoffSqlHelper`** — A shared helper that generates the SQL for saving and reading signoffs. Used by many rule services to avoid repeating the same signoff database code.

### 7.3 Rule Services (Rule 11 to Rule 65)

Each rule has a service that does three main things:

**Step 1 — Discovery:** Connects to the HEMIS SQL Server and lists available databases, tables, and columns so the user can configure the rule without having to type anything manually.

**Step 2 — Validation:** Builds and executes a T-SQL query against the HEMIS data. The query uses temporary tables, CTEs, JOINs, and CASE expressions to classify each row as PASS or FAIL. Results are stored back in the system database so they can be downloaded later.

**Step 3 — Workspace Management:** Saves the connection settings, column mappings, and results so that next time a user opens the same client's Rule 65 page, everything is restored exactly as they left it.

### 7.4 RScript Generators-I WILL DELETE THIS AS IS NO LONGER NEEDED

Files like `Rule65RScriptGenerator.cs` generate the **R statistical script** equivalent of the SQL validation — a script that an analyst could run in R if they needed to. These are available as downloads and give technical reviewers an independent way to verify results.

---

## 8. View Models

**Files:** `ViewModels/Rule65ViewModels.cs` (one file per rule)

View models are **data packages** — C# classes that carry information from a service to a view (the screen). Think of them as the waiter carrying a tray from the kitchen to the table.

They are separate from models (which represent database rows) because the screen often needs a combination of data, calculated values, and display-specific flags that don't belong in the database.

### 8.1 Typical View Model Structure for a Rule

**Request model** — what the browser sends when running a validation:
- Server name, database, driver
- Table names and column mappings

**Validation summary** — what the service returns after running:
- `TotalCount`, `PassCount`, `FailCount`
- `ExceptionRate` — the percentage of rows that failed
- `Status` — "PASS" or "FAIL"
- `FailRows` — the failing rows (up to 10 shown in browser, full set stored in DB)
- `PassRows` — the passing rows
- `ExceptionCategories` — a breakdown by failure type with counts

**Workspace state** — what's loaded when a user returns to a saved run:
- All the connection and column settings
- The saved summary
- Signoff information

**Signoff / review models** — information about who has signed off, when, and with what comment.

---

## 9. Views

Views are the **screens** — the HTML pages that users see. They use Razor syntax, which is HTML with C# embedded in it using `@` symbols.

### 9.1 Shared Layout — `Views/Shared/_Layout.cshtml`

This is the master page. Every other page inherits from it. It contains:
- The navigation bar
- The sidebar
- The session management (login/logout)
- Global JavaScript functions used across all pages:
  - `window.fetchJsonWithProgress` — makes API calls with a progress spinner
  - `window.downloadFileWithProgress` — downloads files with a progress indicator
  - `window.ruleWorkspaceUi` — manages button states (enabled/disabled/readonly) based on signoff status

Because these functions are defined in the layout, every rule page can use them without copying any code.

### 9.2 Module Navigation — `Views/Shared/_ModuleSequenceNav.cshtml`

The breadcrumb navigation shown on every rule page that lets you move to the previous or next rule in sequence.

### 9.3 Account Views — `Views/Account/`

Login, forgot password, reset password, change password, and the password-expired screens.

### 9.4 Admin Views — `Views/Admin/`

- `Clients.cshtml` — the list of all engagements
- `ClientDetail.cshtml` — the engagement hub page showing all users and all rule modules as clickable cards
- `Users.cshtml` — the list of all users
- `CreateClient.cshtml`, `CreateUser.cshtml`, `EditUser.cshtml` — forms
- `AuditLog.cshtml` — the activity history table

### 9.5 Dashboard View — `Views/Dashboard/Index.cshtml`

The home screen showing all the client cards the logged-in user can access, with a grid of rule status indicators.

### 9.6 Rule Views — `Views/Rule65/` (repeated for every rule)

Each rule has two views:

**`Index.cshtml`** — the interactive workspace. This is the main page where all the work happens. It contains:
- A connection section (server, database, driver)
- A configuration section (table and column selection)
- An action bar (Connect, Verify, Run, Generate SQL buttons)
- A results panel with tabs: Analysis | FLAGGED | CLEAR | SQL | Downloads
- A workspace card (shows run ID, status, who has signed off)
- A signoff section (comment box and signoff/remove-signoff buttons)

The page uses **JavaScript** heavily. When you click "Run Validation", the browser calls the API, gets results back as JSON, and renders the tables and statistics on the page without reloading. This is what makes the system feel fast and interactive.

**`Run.cshtml`** — a read-only summary view of a completed run. This is what a Manager or Director sees when they open a completed validation to review it. It shows the statistics, the exception breakdown, and the row samples — but no editable controls.

### 9.7 Messages View — `Views/Messages/Index.cshtml`

The internal messaging inbox. Shows sent and received messages with file attachments.

---

## 10. Helpers

Helpers are small utility classes used throughout the system.

### 10.1 `IntegrityRuleCatalog`

A static dictionary that stores the title, short description, detailed description, validation criteria, and required tables for Rules 1–10. Used when displaying rule information on the dashboard and client detail pages.

### 10.2 `RuleRouteHelper`

Knows the URL for every rule. When you need to link to Rule 65's workspace page, you call `RuleRouteHelper.GetWorkspaceUrl(65, clientId)` instead of building the URL string manually. This means if a URL ever changes, you only change it in one place.

### 10.3 `ValidationRunAccessPolicy`

The **permission rulebook**. A set of static methods that answer questions like:
- "Can this user sign off?" → Only DataAnalyst, Manager, and Director can
- "Can this user download results?" → DataAnalyst, Manager, Director, and Trainee can
- "Can this user see results?" → Only after the DataAnalyst has signed off (for non-DataAnalyst roles)
- "Can this user remove a signoff?" → Only the person who placed it

This is checked both on the server (in the controller) and on the client (in the JavaScript) to ensure the buttons show the right state.

### 10.4 `ModuleSequenceNavigationHelper`

Knows the order of all rules and generates the "Previous Rule / Next Rule" navigation links. It also knows which rules have a `/Run/` view and adjusts the links accordingly.

### 10.5 `AvatarHelper`

Generates initials-based avatar text (e.g. "MM" for "Mamishi Madire") for the profile picture placeholder when no photo has been uploaded.

### 10.6 `NumericFilterValueHelper`

Parses filter values typed by users into numeric ranges. For example, if a user types ">1000" it returns a numeric comparison condition for the SQL query.

---

## 11. Filters

**`Filters/PasswordAgeFilter.cs`**

This is an **action filter** — it runs automatically before every controller action. Its job is to check whether the logged-in user's password has expired. If it has, the user is redirected to the password renewal page instead of allowing them to continue. This is a security control that ensures passwords are rotated regularly.

---

## 12. The Validation Rules — What Each One Checks

### Rules 1–10 — Basic Integrity Checks
These run directly against the HEMIS tables on the institution's own database.

| Rule | What it checks |
|---|---|
| 1 | Qualifications without a qualification type |
| 2 | Qualifications without an approval status |
| 3 | Duplicate qualification codes |
| 4 | Duplicate course codes |
| 5 | Invalid/placeholder student numbers (9999999) |
| 6 | Students without a foundation indicator |
| 7 | Students linked to invalid qualifications |
| 8 | Course registrations for courses that don't exist |
| 9 | Course registrations for students who don't exist |
| 10 | Review of all table join keys (a joining rules audit) |

### Rules 11–32 — Data Quality Checks
These rules go deeper into the quality of specific data fields — checking for blank values, invalid codes, out-of-range dates, inconsistencies between tables, and so on. Each rule targets a specific table and field combination.

### Rules 34–41 — Student and Programme Validation
Cross-checking student records against programme data, checking enrolment status, checking qualification completion dates, and verifying that students are registered in valid programmes.

### Rules 44–48 — Assessment and Examination Checks
Validating assessment records, marks ranges, examination participation, and results consistency.

### Rules 51–55 — Staff and Employment Data
Checking PROF (staff/personnel) data — staff qualifications, staff numbers, employment types, and academic staff records.

### Rules 57–63 — CESM and Course Data
Verifying that courses are correctly classified by CESM (Classification of Educational Subject Matter) codes, and that course-level data is consistent.

### Rule 64 — Cancellation Date Validation
Checks that when a student cancels, the cancellation date is not the same as the census date for that record. A cancellation on the census date would affect subsidy calculations.

### Rule 65 — Cancellation Census Date Validation
An extension of Rule 64. Checks cancellation records against two dates:
- **Cancel Date = Census Date (row level):** FAIL — student cancelled on the exact census date
- **Cancel Date = Current Census Date (institution level):** FAIL — student cancelled on a date that appears as the current census date in `CENSUS_LIST_CLIENT`
- **Anything else:** PASS

---

## 13. The Workspace and Signoff Workflow

This is one of the most important concepts in the system. Here is how it works step by step:

**1. First Visit**
When a Data Analyst opens a rule page for a client for the first time, the page is empty. They configure the connection and run the validation. Results appear on screen.

**2. Saving a Workspace**
When they click "Save Workspace", the system stores:
- The server, database, driver, and table/column settings
- The full results (thousands of rows) in a compressed JSON blob in the system database
- A `ValidationRuns` record in the app database

**3. Returning Later**
If the analyst closes the browser and comes back the next day, the page automatically loads the saved configuration and results. They don't have to re-run anything.

**4. Data Analyst Signoff**
When the analyst is satisfied, they type a comment and click "Save Signoff". Their name, role, comment, and timestamp are saved. The results are now "locked" — no more editing.

**5. Manager/Director Signoff**
After the DataAnalyst has signed off, Managers and Directors can view the results (they couldn't before — `CanViewSignedResults` enforces this). They can add their own signoff comments.

**6. Downloading**
Excel, CSV, and SQL downloads are always available. Excel exports have multiple tabs:
- Summary (configuration details and totals)
- Exception Breakdown (counts per category)
- Flagged Rows (all failing rows)
- Clear Rows (all passing rows)
- One tab per exception category

---

## 14. Security

### 14.1 Authentication
ASP.NET Identity handles login. Passwords are hashed using BCrypt (a strong one-way hashing algorithm). No password is ever stored in plain text.

### 14.2 Authorisation
The `[Authorize]` attribute on controllers ensures no page can be accessed without being logged in. The `[Authorize(Roles = "Admin")]` attribute restricts certain pages to Admin users only.

### 14.3 Anti-Forgery Tokens
Every form and every POST API call requires an anti-forgery token (also called a CSRF token). This prevents attackers from tricking a user's browser into submitting requests on their behalf. In the JavaScript, this token is read from a hidden `<input>` and sent in every request header.

### 14.4 Session Security
Sessions expire after 8 hours. Cookies are marked `HttpOnly` (cannot be read by JavaScript) and `SameSite` (cannot be sent by cross-origin requests).

### 14.5 SQL Injection Prevention
All HEMIS database queries use **parameterized queries** — values are passed as parameters, never embedded directly into SQL strings. This means even if a user enters malicious input, it cannot alter the SQL query.

### 14.6 Password Policy
- Minimum 8 characters with complexity requirements
- Account lockout after 5 failed attempts
- Password expiry enforced by the `PasswordAgeFilter`
- Password history tracked to prevent reuse

---

## 15. NuGet Packages — Third-Party Libraries Used

| Package | What it does | Why it's needed |
|---|---|---|
| `Microsoft.EntityFrameworkCore.Sqlite` | Connects to SQLite | Stores users, clients, and audit logs locally |
| `Microsoft.EntityFrameworkCore.SqlServer` | Connects to SQL Server via EF Core | Used for app-level SQL Server access |
| `Microsoft.Data.SqlClient` | Connects to SQL Server directly | Used for all HEMIS database queries (raw SQL, faster than EF for large datasets) |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | User login, passwords, roles | The entire authentication system |
| `ClosedXML` | Creates Excel (.xlsx) files | Generates the multi-tab Excel export files |
| `Newtonsoft.Json` | Converts objects to/from JSON | Used to serialize results when saving to the database and sending to the browser |
| `Microsoft.AspNetCore.Mvc.NewtonsoftJson` | Makes MVC use Newtonsoft | Ensures consistent JSON formatting with camelCase |
| `BCrypt.Net-Next` | Password hashing | Securely hashes passwords during initial seeding |

---

## 16. The Two-Database Design (Why It Exists)

This is worth understanding because it's different from most systems.

**Database 1 — SQLite (the application database):**
A small file stored on the server. Holds only: users, roles, clients, assignments, validation run records, and audit logs. SQLite is simple, requires no installation, and works perfectly for this kind of structured, low-volume data.

**Database 2 — SQL Server (the HEMIS system database):**
A SQL Server database that stores: validation results, workspace configurations, workspace state, review signoffs, and the detailed row-level results from each validation run. This is needed because:
- Results can contain hundreds of thousands of rows — too large for SQLite
- SQL Server can use compressed JSON columns, indexed queries, and partitioning for performance
- It stays separate from the identity data, which is a good security practice

**Database 3 — The Institution's HEMIS SQL Server:**
This belongs to the institution. Your system connects to it only when running a validation — it reads data, runs the validation SQL, then disconnects. It never writes anything to the institution's database.

---

## 17. How the Progress Spinner Works

When you click "Run Validation" on a rule with a large dataset, the query can take 30–60 seconds. Rather than making the browser wait with a blank screen, the system uses a **polling pattern**:

1. The browser sends the validation request and gets back a **job ID** immediately
2. The query runs in the background on the server
3. The browser polls `/ValidationOperations/GetStatus?jobId=...` every second
4. The server responds with the current progress percentage and status message
5. When the job is done, the server returns the full results
6. The browser stops polling and renders the results

The `PendingValidationCacheService` holds the in-memory state of running jobs. The `ValidationOperationService` coordinates the actual background work.

---

## 18. How Excel Exports Work

When you click "Download Excel":

1. The browser sends a POST request with the `savedRunId` (the ID of the saved validation run)
2. The controller calls `ResolveDownloadSummaryAsync(runId)` — this loads the **full** results from the system database (not the 10-row browser preview, but all thousands of rows)
3. `BuildExcelExport` creates an in-memory Excel workbook using ClosedXML
4. Worksheets are added: Summary, Exception Breakdown, Flagged Rows, Clear Rows, then one sheet per exception category
5. The workbook is saved to a `MemoryStream` (never written to disk)
6. The bytes are returned as a file download

The same pattern applies to CSV and SQL downloads.

---

## 19. Folder Structure Summary

```
HemisAudit/
├── Program.cs                    ← Startup: configure services, middleware, routes
├── HemisAudit.csproj             ← Project file: NuGet packages listed here
│
├── Controllers/                  ← Traffic directors — receive requests, return responses
│   ├── AccountController.cs      ← Login, logout, password management
│   ├── AdminController.cs        ← Client/user management (Admin only)
│   ├── DashboardController.cs    ← Home screen
│   ├── MessagesController.cs     ← Internal messaging
│   ├── ProfileController.cs      ← Edit own profile
│   ├── Rule10Controller.cs       ← Rules 1–10 (shared controller)
│   ├── Rule11Controller.cs       ← Rule 11 (one controller per rule)
│   ├── ...
│   ├── Rule65Controller.cs       ← Rule 65
│   └── ValidationOperationsController.cs  ← Background job progress
│
├── Services/                     ← The workers — real logic lives here
│   ├── SystemDatabaseService.cs  ← All system DB interactions
│   ├── AuditLogService.cs        ← Logging
│   ├── EmailService.cs           ← SMTP emails
│   ├── PasswordPolicyService.cs  ← Password expiry checks
│   ├── ExportService.cs          ← Shared Excel/CSV generator
│   ├── IRule65Service.cs         ← Interface (contract)
│   ├── Rule65Service.cs          ← Implementation (actual code)
│   ├── Rule65RScriptGenerator.cs ← Generates equivalent R script
│   └── ...
│
├── ViewModels/                   ← Data packages passed between service and view
│   ├── ApplicationViewModels.cs  ← Shared view models (login, admin, dashboard)
│   └── Rule65ViewModels.cs       ← Rule 65 specific (request, summary, workspace, signoff)
│
├── Models/                       ← Database table definitions
│   └── ApplicationModels.cs      ← User, Client, ClientUser, ValidationRun, AuditLog
│
├── Data/                         ← Database setup
│   ├── ApplicationDbContext.cs   ← EF Core context (SQLite gateway)
│   ├── DbInitializer.cs          ← Seeds admin user on first run
│   └── SystemDatabaseBootstrapper.cs ← Creates system DB tables
│
├── Helpers/                      ← Small utility tools
│   ├── IntegrityRuleCatalog.cs   ← Rules 1–10 metadata
│   ├── RuleRouteHelper.cs        ← URL builder for all rules
│   ├── ValidationRunAccessPolicy.cs  ← Permission checks
│   ├── ModuleSequenceNavigationHelper.cs ← Prev/Next rule navigation
│   ├── AvatarHelper.cs           ← Initials for profile pictures
│   └── NumericFilterValueHelper.cs   ← Parses filter values
│
├── Filters/                      ← Code that runs before/after controller actions
│   └── PasswordAgeFilter.cs      ← Redirects expired passwords
│
└── Views/                        ← The screens users see
    ├── Shared/
    │   ├── _Layout.cshtml        ← Master page (nav, global JS)
    │   └── _ModuleSequenceNav.cshtml  ← Prev/Next navigation bar
    ├── Account/                  ← Login, password screens
    ├── Admin/                    ← Admin management screens
    ├── Dashboard/                ← Home screen
    ├── Messages/                 ← Messaging inbox
    ├── Profile/                  ← Profile editor
    ├── Rule65/
    │   ├── Index.cshtml          ← Interactive workspace
    │   └── Run.cshtml            ← Read-only completed run view
    └── ...
```

---

## 20. Key Concepts to Remember

**Dependency Injection** — Instead of writing `new Rule65Service()`, you declare `IRule65Service` in the constructor and the system hands one to you automatically. This keeps the code clean and testable.

**Async/Await** — Almost every method that touches a database or the network is `async`. This means the server can handle other requests while waiting for the database to respond, instead of sitting idle.

**Razor** — The template engine used in `.cshtml` files. You write HTML with `@Model.SomeProperty` to insert data from the server.

**CSRF (Cross-Site Request Forgery) protection** — The `@Html.AntiForgeryToken()` in forms and the `RequestVerificationToken` header in JavaScript calls protect every state-changing operation.

**BrowserPreviewRowLimit** — Only 10 rows are sent to the browser after a validation run (for speed). The full dataset is saved in the database. When you download Excel or CSV, the controller fetches the full dataset from the database.

**Workspace = saved state** — The workspace is everything about a rule run for a specific client: the connection settings, the column mappings, the results, the signoffs. It means an analyst can close the browser, come back a week later, and see everything exactly as they left it.

---

*This document describes the HEMIS Audit System as built on .NET 8 / ASP.NET Core MVC.*
*Last updated: June 2026*

---

# PART 2 — Why Each Piece Matters

This section explains **why** each part of the system exists. What would break if it wasn't there? What problem does it solve? Think of this as the "importance" section.

---

## Controllers — Why They Matter

**Imagine a government office.** People (browsers) walk in with requests. The receptionist (controller) listens, takes the request to the right department (service), and brings back the answer. Without controllers, your browser would have no way to talk to your system.

Every controller in this project is important for a specific reason:

| Controller | Why it is important |
|---|---|
| `AccountController` | Without this, nobody can log in or out. It is the front door of the entire system. If this breaks, the whole system is inaccessible. |
| `AdminController` | Without this, nobody can create clients or assign users. No clients = no work can be done. This is the management backbone. |
| `DashboardController` | The first screen users see after login. Without it, users would land on a blank page with no direction. |
| `MessagesController` | Allows team members to communicate inside the system without using external email. Keeps all audit-related communication in one place. |
| `ProfileController` | Lets users personalise their accounts. Also important for identification — when signoffs show "Mamishi Madire", that name comes from the profile. |
| `Rule10Controller` | Runs the 10 foundational HEMIS integrity checks. Without these, the most basic data quality problems (duplicates, missing fields, invalid codes) would go undetected. |
| `Rule11 to Rule65 Controllers` | Each one handles one specific audit check. Without any single controller, that rule cannot be run — and that type of data error cannot be caught. Every rule protects a different part of the HEMIS submission. |
| `ValidationOperationsController` | Without this, large validations would make the browser time out and show an error. This controller is what makes the system feel responsive and professional when queries take a long time. |

---

## Services — Why They Matter

**The service is where the real work happens.** If the controller is the receptionist, the service is the expert in the back room who actually does the job.

If you removed the services, your controllers would have nothing to call — the buttons would exist but pressing them would do nothing.

| Service | Why it is important |
|---|---|
| `SystemDatabaseService` | This is the most critical service in the system. It manages all connections to the system SQL Server database — saving results, loading workspaces, reading signoffs. Without it, nothing can be saved or retrieved. Every rule service calls this. |
| `AuditLogService` | Creates a permanent paper trail. Without it, you would have no record of who ran what, when, and on which client. In an audit firm, this trail is not optional — it is a professional and legal requirement. |
| `EmailService` | Sends password reset emails. Without it, if a user forgets their password, they are permanently locked out with no way to recover. |
| `PasswordPolicyService` | Enforces security. Without it, users could keep the same password forever, which increases the risk of unauthorised access. |
| `PendingValidationCacheService` | Holds the in-memory state of running background jobs. Without it, the progress spinner would never work — the browser would have no way to know if a validation is still running or has finished. |
| `ValidationOperationService` | Runs validations in the background. Without it, a validation that takes 45 seconds would cause the browser to time out and show an error. This service is what separates a professional system from a simple script. |
| `ExportService` | Generates Excel and CSV files. Without it, analysts would have to manually copy results from the screen — which defeats the purpose of the system. |
| `ReviewSignoffSqlHelper` | Shared SQL for signoffs. Without it, every single rule service would have to contain the same signoff code — meaning any bug fix would need to be applied 55 times. This service prevents that duplication. |
| `Rule65Service` (and all other Rule services) | These are the engines of each rule. They build and run the actual T-SQL validation queries. Without the service, the rule does not run. Without the rule running, that type of error goes undetected in the HEMIS submission. |
| `Rule65RScriptGenerator` (and others) | Generates an R-language version of the validation. Important because it gives technical reviewers an independent way to verify that the SQL results are correct — a second opinion using a completely different tool. |

---

## Models — Why They Matter

**Models are the language your system uses to talk to the database.** Without models, your C# code would have no structured way to read or write data.

| Model | Why it is important |
|---|---|
| `ApplicationUser` | Stores who can use the system. Without this, there are no accounts, no login, and the system cannot know who is doing what. |
| `Client` | Represents an institution engagement. Without this, there is nowhere to attach work — no client means no context for any validation. |
| `ClientUser` | Links users to clients with roles. Without this, every user would see every client's data — there would be no separation between engagements. A user at TUT should not see UNISA's results. |
| `ValidationRun` | Records that a rule was run for a client. Without this, there would be no history — every time a user visits a rule page, it would be blank with no memory of previous runs. |
| `AuditLog` | A record of every action. Without this, if something goes wrong (wrong data deleted, incorrect signoff), there is no way to trace what happened or who is responsible. |

---

## Data Layer — Why It Matters

The data layer (`Data/` folder) is the **foundation** that everything sits on.

| File | Why it is important |
|---|---|
| `ApplicationDbContext` | This is the bridge between C# and SQLite. Without it, Entity Framework has no idea what tables exist or how they relate to each other. It is the definition of the database structure. |
| `DbInitializer` | Seeds the Admin user on first launch. Without this, a brand new installation would have no Admin account, and nobody could ever log in to set up the system. It is the "first user" creator. |
| `SystemDatabaseBootstrapper` | Creates the system database tables on startup. Without this, the tables for storing workspace state, results, and signoffs would not exist, and every attempt to save a workspace would fail. |

---

## Helpers — Why They Matter

Helpers are **tools used in many different places**. The key word is "reused". Without helpers, the same code would be copied into 55 different controllers — and every bug fix would need to be applied 55 times.

| Helper | Why it is important |
|---|---|
| `IntegrityRuleCatalog` | Stores the titles and descriptions for Rules 1–10 in one central place. Without it, every page that mentions Rule 3 would have to hard-code "Duplicate qualification codes". If the wording needs to change, it would need to change in 10 places instead of one. |
| `RuleRouteHelper` | Knows the URL for every rule. Without it, every link to a rule page would be a hard-coded string. If a URL ever changed, the link would be wrong everywhere it appears. |
| `ValidationRunAccessPolicy` | Enforces who can do what. Without this, there would be no permission checks — a Trainee could sign off, a Manager could see unsigned results, anyone could remove any signoff. The integrity of the review workflow depends on this helper. |
| `ModuleSequenceNavigationHelper` | Powers the Prev/Next navigation. Without it, the only way to move between rules would be to go back to the dashboard. This makes the system much faster for analysts who work through rules sequentially. |
| `AvatarHelper` | Generates initials for profile pictures. Without it, users without a photo would see a broken image icon. A small detail, but it makes the system look professional. |
| `NumericFilterValueHelper` | Parses filter conditions like ">500" or "between 100 and 200". Without it, analysts could not filter large result sets by numeric criteria — they would have to scroll through thousands of rows manually. |

---

## Filters — Why They Matter

| Filter | Why it is important |
|---|---|
| `PasswordAgeFilter` | Runs before every single page load. Without it, a user whose password expired 6 months ago could still use the system indefinitely. Password rotation is a minimum security standard in any professional environment. This filter is what enforces it automatically without relying on users to remember. |

---

## Views — Why They Matter

**Views are the only part of the system the user ever sees.** Everything else is invisible plumbing. If the views are broken, the system might work perfectly behind the scenes — but the user would have no way to use it.

| View | Why it is important |
|---|---|
| `_Layout.cshtml` | The master template. Without it, every page would need its own navigation bar, its own CSS, its own JavaScript. It is what makes the system look and feel consistent. The global functions (`fetchJsonWithProgress`, `downloadFileWithProgress`, `ruleWorkspaceUi`) defined here are used by all 55 rule pages. |
| `_ModuleSequenceNav.cshtml` | The rule navigation bar. Without it, moving between rules would require going back to the dashboard every time. |
| `Account/Login.cshtml` | The entry point for all users. Without it, nobody can access the system at all. |
| `Admin/ClientDetail.cshtml` | The engagement hub. Without it, there would be no single place to see all rule modules for a client. This is the page the Admin and Data Analyst use most. |
| `Admin/AuditLog.cshtml` | Makes the audit trail visible. Without this view, even though the data is being logged, nobody could see it. |
| `Dashboard/Index.cshtml` | Shows the user their work. Without it, a logged-in user would have no idea what clients they are assigned to or which rules have been completed. |
| `Rule65/Index.cshtml` | The complete validation workspace for Rule 65. Without it, Rule 65 cannot be run at all — there is no interface. This is the most complex view in the system: connection manager, column mapping, live results with tabs, workspace management, and signoff — all in one page. |
| `Rule65/Run.cshtml` | The read-only review view. Without it, Managers and Directors would have to use the same interactive workspace — which is confusing and risky. The Run view shows exactly what was validated without any editable controls. |

---

## ViewModels — Why They Matter

ViewModels are the **messengers** that carry data from services to views.

Without ViewModels, you would have two bad options:
1. Pass raw database objects directly to the view — which exposes sensitive fields and makes the view responsible for calculations it shouldn't know about
2. Pass raw strings and numbers — which loses all structure and type safety

ViewModels solve this by creating a clean, purpose-built package of exactly the data the view needs, in exactly the format it needs.

For example, `Rule65ValidationSummary` carries:
- `TotalCount`, `PassCount`, `FailCount` — pre-calculated totals the view just displays
- `ExceptionRate` — already calculated as a percentage
- `Status` — already determined as "PASS" or "FAIL"
- `FailRows` — already filtered, sorted, and limited to the preview count
- `ExceptionCategories` — already grouped and counted

Without the ViewModel, the view would have to do all this calculation itself — mixing display logic with business logic, which is poor design and hard to maintain.

---

## The `obj` Folder — What It Is

The `obj` folder is **automatically generated by the build system**. You did not write anything in it. It contains:
- Compiled versions of your Razor views (`.cshtml` → `.cs`)
- Assembly information files
- Build metadata

You should never edit anything in `obj`. It is recreated every time you build. It exists purely for the compiler. If you delete it, the next build will recreate it.

---

## The `.run` Folder — What It Is

The `.run` folder contains **runtime support files** that are not part of the compiled application:
- Data protection keys (for encrypting cookies securely)
- Any test/helper scripts used during development

The data protection keys are critical for security — if you move the application to a new server, you need to bring these keys with you, otherwise all existing login sessions and any encrypted data will be invalidated.

---

## Summary — The Chain of Importance

Every piece of the system depends on the pieces below it:

```
User clicks a button in the BROWSER
        ↓
VIEW sends a request (with anti-forgery token)
        ↓
CONTROLLER receives the request and validates it
        ↓
SERVICE does the real work (queries, calculations, SQL)
        ↓
MODEL / DATA LAYER reads/writes to the database
        ↓
DATABASE stores or returns the data
        ↓
SERVICE packages results into a VIEWMODEL
        ↓
CONTROLLER passes ViewModel to VIEW
        ↓
VIEW renders the result for the USER
```

Remove any one link in this chain and the system stops working at that point. That is why every layer exists and why every layer is important.

**HELPERS** sit beside this chain and are used by multiple layers.
**FILTERS** wrap around the controller and intercept requests before they arrive.
**MODELS** define the shape of data at the database layer.
**VIEWMODELS** define the shape of data at the view layer.

---

*End of documentation.*
