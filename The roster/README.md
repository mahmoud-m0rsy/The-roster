# Client Showcase

A premium public roster of your clients plus a private admin dashboard to manage them.
Built with ASP.NET Core 10 (Razor Pages) and SQLite. No other services are required.

## Run it

```powershell
cd ClientShowcase
dotnet run
```

Open http://localhost:5127.

**First run:** the console prints a one-time **setup code** (it is also saved in
`App_Data/setup-code.txt`). Go to http://localhost:5127/admin/setup, enter the code, and
create your admin username and password. After that, the setup page is permanently closed and
the dashboard lives at `/admin`.

**Or create the first admin from settings** (handy on a host where you can't see the console):
set `InitialAdmin:Username` and `InitialAdmin:Password` (as environment variables:
`InitialAdmin__Username` and `InitialAdmin__Password`). On startup the account is created with a
hashed password if no admin with that username exists yet; an existing account is never changed.
The password must be at least 10 characters. Once you can sign in, remove `InitialAdmin__Password`.
Never put real credentials in `appsettings.json` or commit them to Git.

## What's where

| URL | Purpose |
| --- | --- |
| `/` | Public homepage: all visible clients in your manual order, with search |
| `/clients/{name}` | Public profile: photos, details, contact, socials, work history |
| `/admin` | Clients & ranking: drag to reorder, type a position, ↑/↓, hide/show, delete |
| `/admin/clients/new` | Add a client with photos |
| `/admin/clients/{id}/edit` | Edit profile, add/reorder/delete photos, choose the profile photo |
| `/admin/clients/{id}/work` | Work history: add, edit, delete, reorder entries (with images and links) |
| `/admin/settings` | Website name, tagline and homepage texts |
| `/admin/account` | Change password |

## Data

Everything is stored in `App_Data/`:

- `showcase.db`: the SQLite database (clients, rankings, work history, settings, admin)
- `media/`: uploaded images (optimized in the browser to WebP, max 2400px, plus a thumbnail; metadata such as GPS is stripped)
- `keys/`: encryption keys for login sessions

**Back up the whole `App_Data` folder.** To keep data somewhere else, set `Storage:DataPath`
in `appsettings.json` (or the `Storage__DataPath` environment variable). Avoid running the live
site from a OneDrive-synced folder, because syncing an open database can cause file-locking problems.

## Security

- Admin pages require login. Passwords are hashed with PBKDF2, and the account locks for 15 minutes after 5 failed attempts.
- Login attempts are rate-limited per IP. All forms are CSRF-protected.
- Changing the password signs out every other session.
- Per client, you can hide the phone number and/or email from the public site. "Private notes" are never shown publicly.
- Hidden clients are not on the homepage, and their profile URL returns 404.
- Uploads are checked by their real file signature (JPG, PNG, WebP, AVIF; 15 MB max each) and stored under random names.
- When deploying, serve the site over **HTTPS** (e.g. behind IIS, Nginx or Caddy, or a host like Azure App Service).

## Publish

```powershell
dotnet publish -c Release -o publish
```

Copy the `publish` folder to the server. `App_Data` is never part of a publish (and Web Deploy is
set to leave the server's `App_Data` alone), so redeploying never overwrites the live database. To move
existing data, upload `showcase.db` and `media/` once, but **not** `keys/` (see below).

### MonsterASP.NET / shared IIS hosting

1. In the control panel open your website → **Scripting → Environment variables** and add:
   - `ASPNETCORE_ENVIRONMENT` = `Production`
   - `InitialAdmin__Username` = your admin username
   - `InitialAdmin__Password` = your admin password (remove it after the first successful sign-in)
   - optional: `Storage__DataPath` = a folder outside the site root for the database, media and keys
2. Click **Restart application**, then sign in at `/admin/login`.
3. When publishing from Visual Studio, keep "Remove additional files at destination" unchecked.

Login-session keys in `App_Data/keys` are stored unencrypted on disk (that folder is never served
over HTTP). Older versions wrapped them with Windows DPAPI, which fails on app pools without a loaded
user profile and broke the login page with HTTP 500. If you ever copied a `keys` folder from your PC to
the server, delete the server's `App_Data/keys` folder and restart. This only signs everyone out; no
data is lost.

To see startup errors on IIS, temporarily set `stdoutLogEnabled="true"` in the deployed `web.config`
(logs go to `.\logs\stdout*`), restart, reproduce, then turn it off again.
