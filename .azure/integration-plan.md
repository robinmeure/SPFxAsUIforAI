# Integration Plan

## Backend
- Folder: `api/`
- Framework: ASP.NET Core minimal API, .NET 10
- Run: `dotnet run --launch-profile https`
- Port: HTTPS 7198, HTTP 5198
- Health: none (stream endpoint is the smoke-test)
- Build: `dotnet restore && dotnet build`

## Frontend
- Folder: `spfx/`
- Build: `npm run build`
- Production build: `npm run build:prod`
- Package: `npm run package`
- API seam: `src/webparts/helloCopilot/components/HelloCopilot.tsx`
- Acquire token with `context.aadTokenProviderFactory.getTokenProvider().getToken(apiResource)`; call streaming API with native `fetch`.

## API routes
- `POST /api/messages/stream` — text/event-stream mock response
- Local mock auth: only `UseMockAuthentication=true`
- Protected mode: JWT bearer + delegated scope policy `AccessAsUser` requiring `scp=access_as_user`

## Database
- No database. No migrations or seed data.

## CORS
- Configure `Cors:AllowedOrigins` in appsettings.
- Local origins include localhost SPFx workbench and Teams.

## Deployment configuration
- Replace API and tenant placeholders in appsettings and web part property defaults.
- Configure an Entra app registration with Application ID URI `api://<api-client-id>` and delegated scope `access_as_user`.
