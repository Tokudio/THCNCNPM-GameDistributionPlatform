# Frontend

This project was generated using [Angular CLI](https://github.com/angular/angular-cli) version 21.2.29.

## Local authentication

Start the Web API with its HTTPS launch profile from the repository root. The API needs its existing Development database and JWT configuration.

```bash
dotnet run --project backend/src/GameDistributionPlatform.WebApi --launch-profile https
```

Run `npm start` from `frontend` and open `http://localhost:4200/`. The Angular Development proxy forwards `/api/**` to `https://localhost:7255`. Production hosting must serve or forward `/api/v1/**` on the same origin as the app.

The app stores the JWT in `sessionStorage` for refreshes in the current tab and verifies it with `/api/v1/auth/me` at startup. Browser scripts can read this storage. Sign out clears the local session immediately. A JWT already issued by the API remains valid until it expires, subject to backend authentication checks.

## Code scaffolding

Angular CLI includes powerful code scaffolding tools. To generate a new component, run:

```bash
ng generate component component-name
```

For a complete list of available schematics (such as `components`, `directives`, or `pipes`), run:

```bash
ng generate --help
```

## Building

To build the project run:

```bash
ng build
```

This will compile your project and store the build artifacts in the `dist/` directory. By default, the production build optimizes your application for performance and speed.

## Running unit tests

To execute unit tests with the [Vitest](https://vitest.dev/) test runner, use the following command:

```bash
npm test -- --watch=false
```

## Running end-to-end tests

For end-to-end (e2e) testing, run:

```bash
ng e2e
```

Angular CLI does not come with an end-to-end testing framework by default. You can choose one that suits your needs.

## Additional Resources

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.
