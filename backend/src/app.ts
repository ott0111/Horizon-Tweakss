import express, { type NextFunction, type Request, type Response } from "express";
import rateLimit from "express-rate-limit";
import type { AuthService, PublicUser } from "./auth-service.js";
import type { AppConfig } from "./config.js";
import { AppError, badRequest, unauthorized } from "./errors.js";
import type { OAuthProviders } from "./providers.js";

declare global {
  namespace Express { interface Locals { user?: PublicUser; } }
}

const asyncRoute = (handler: (request: Request, response: Response, next: NextFunction) => Promise<unknown>) =>
  (request: Request, response: Response, next: NextFunction) => { void handler(request, response, next).catch(next); };

export function createApp(config: AppConfig, auth: AuthService, providers: OAuthProviders) {
  const app = express();
  if (config.trustProxy) app.set("trust proxy", 1);
  app.disable("x-powered-by");
  app.use((_request, response, next) => {
    response.setHeader("Cache-Control", "no-store");
    response.setHeader("X-Content-Type-Options", "nosniff");
    next();
  });
  app.use(express.json({ limit: "32kb" }));

  const strictLimiter = () => rateLimit({
    windowMs: 15 * 60_000,
    limit: 20,
    standardHeaders: "draft-8",
    legacyHeaders: false,
    message: { error: { code: "TOO_MANY_REQUESTS", message: "Too many attempts. Please wait a few minutes and try again." } }
  });
  const token = (request: Request) => request.headers.authorization?.match(/^Bearer\s+(.+)$/i)?.[1];
  const requireUser = asyncRoute(async (request, response, next) => {
    response.locals.user = await auth.authenticate(token(request));
    next();
  });

  app.get("/health", asyncRoute(async (_request, response) => { await auth.health(); response.json({ status: "ok", service: "horizon-auth", database: "ok" }); }));
  app.get("/v1/auth/providers", (_request, response) => response.json({ data: auth.providerStatuses() }));
  app.post("/v1/auth/register", strictLimiter(), asyncRoute(async (request, response) => {
    const result = await auth.register({ ...request.body, userAgent: request.get("user-agent") });
    response.status(201).json({ data: result });
  }));
  app.post("/v1/auth/login", strictLimiter(), asyncRoute(async (request, response) => {
    response.json({ data: await auth.login({ ...request.body, userAgent: request.get("user-agent") }) });
  }));
  app.post("/v1/auth/refresh", strictLimiter(), asyncRoute(async (request, response) => {
    response.json({ data: await auth.refresh(request.body?.refreshToken, request.body?.deviceName, request.get("user-agent")) });
  }));
  app.post("/v1/auth/logout", asyncRoute(async (request, response) => {
    await auth.logout(request.body?.refreshToken);
    response.status(204).end();
  }));
  app.post("/v1/auth/forgot-password", strictLimiter(), asyncRoute(async (request, response) => {
    response.json({ data: { accepted: true, ...await auth.requestPasswordReset(request.body?.email) } });
  }));
  app.post("/v1/auth/reset-password", strictLimiter(), asyncRoute(async (request, response) => {
    await auth.resetPassword(request.body?.token, request.body?.password);
    response.json({ data: { reset: true } });
  }));
  app.post("/v1/auth/verify-email", strictLimiter(), asyncRoute(async (request, response) => {
    await auth.verifyEmail(request.body?.token);
    response.json({ data: { verified: true } });
  }));
  app.post("/v1/auth/desktop/exchange", strictLimiter(), asyncRoute(async (request, response) => {
    response.json({ data: await auth.exchangeDesktopCode(request.body?.code, request.body?.redirectUri, request.body?.deviceName, request.get("user-agent")) });
  }));
  app.get("/v1/auth/me", requireUser, (_request, response) => response.json({ data: response.locals.user }));
  app.patch("/v1/account/profile", requireUser, asyncRoute(async (request, response) => {
    response.json({ data: await auth.updateProfile(response.locals.user!.id, request.body ?? {}) });
  }));
  app.put("/v1/account/onboarding", requireUser, asyncRoute(async (request, response) => {
    response.json({ data: await auth.updateOnboarding(response.locals.user!.id, request.body ?? {}) });
  }));

  app.post("/v1/auth/oauth/:provider/start", strictLimiter(), asyncRoute(async (request, response) => {
    const provider = Array.isArray(request.params.provider) ? request.params.provider[0] : request.params.provider;
    if (!provider || !providers.isProvider(provider)) throw badRequest("UNKNOWN_PROVIDER", "That sign-in option is not supported.");
    response.json({ data: await auth.startOAuth(provider, request.body?.desktopRedirectUri, request.body?.clientState) });
  }));
  app.post("/v1/account/link/:provider/start", requireUser, strictLimiter(), asyncRoute(async (request, response) => {
    const provider = Array.isArray(request.params.provider) ? request.params.provider[0] : request.params.provider;
    if (!provider || !providers.isProvider(provider)) throw badRequest("UNKNOWN_PROVIDER", "That sign-in option is not supported.");
    response.json({ data: await auth.startOAuth(provider, request.body?.desktopRedirectUri, request.body?.clientState, response.locals.user!.id) });
  }));
  app.get("/v1/auth/oauth/:provider/callback", asyncRoute(async (request, response) => {
    const provider = Array.isArray(request.params.provider) ? request.params.provider[0] : request.params.provider;
    if (!provider || !providers.isProvider(provider)) throw badRequest("UNKNOWN_PROVIDER", "That sign-in option is not supported.");
    const state = request.query.state;
    if (typeof request.query.error === "string") {
      const redirect = await auth.oauthDenied(provider, state, request.query.error);
      response.redirect(302, redirect);
      return;
    }
    const result = await auth.completeOAuth(provider, state, request.query.code);
    response.redirect(302, result.redirectUri);
  }));

  app.use((_request, _response, next) => next(new AppError(404, "NOT_FOUND", "That Horizon request is not available.")));
  app.use((error: unknown, _request: Request, response: Response, _next: NextFunction) => {
    const malformedJson = error instanceof SyntaxError && "status" in error && error.status === 400;
    const known = error instanceof AppError
      ? error
      : malformedJson
        ? new AppError(400, "MALFORMED_JSON", "That request could not be read. Please try again.")
        : new AppError(500, "INTERNAL_ERROR", "Horizon could not complete that request.");
    if (!(error instanceof AppError) && !malformedJson) console.error(error);
    response.status(known.status).json({ error: { code: known.code, message: known.message } });
  });
  return app;
}
