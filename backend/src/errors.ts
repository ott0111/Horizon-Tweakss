export class AppError extends Error {
  constructor(public readonly status: number, public readonly code: string, message: string) {
    super(message);
    this.name = "AppError";
  }
}

export const badRequest = (code: string, message: string) => new AppError(400, code, message);
export const unauthorized = (message = "Authentication is required.") => new AppError(401, "UNAUTHORIZED", message);
export const unavailable = (code: string, message: string) => new AppError(503, code, message);
