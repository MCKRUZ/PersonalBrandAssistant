# Section 07 Angular - Review Interview (Autonomous Triage)

## Auto-Fix Items

1. **HIGH-1**: Fix @angular/animations version to ^19.2.0
2. **HIGH-2**: Add app.routes.spec.ts with routing tests
3. **HIGH-3**: Add wildcard ** route redirecting to dashboard
4. **HIGH-4**: Scope API key interceptor to apiUrl origin only
5. **MEDIUM-7**: Fix StatusBadge test to verify severity attribute
6. **MEDIUM-9**: Add afterEach restore for environment mutation
7. **MEDIUM-11**: Add ARIA attributes to sidebar and toggle button
8. **LOW-14**: Handle network error (status 0) in error interceptor

## Let Go Items

- **HIGH-5**: Eager child imports acceptable for tiny route files
- **MEDIUM-6**: console.error at bootstrap is standard Angular
- **MEDIUM-10**: Bare MessageService is valid Angular shorthand
- **MEDIUM-12**: AppComponent is a shell with minimal logic
- **LOW-13**: object type for body is fine for now
- **LOW-16**: SCSS variables are reference values, not PrimeNG tokens
