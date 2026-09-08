import nodemailer from "nodemailer";
import type { AppConfig } from "./config.js";

export class Mailer {
  private readonly transporter;
  constructor(private readonly config: AppConfig) {
    this.transporter = config.emailMode === "smtp" ? nodemailer.createTransport({
      host: config.smtp.host, port: config.smtp.port, secure: config.smtp.secure,
      requireTLS: !config.smtp.secure,
      auth: { user: config.smtp.user, pass: config.smtp.password }
    }) : null;
  }

  async sendVerification(email: string, token: string): Promise<void> {
    await this.deliver(email, "Verify your Horizon email", `Your Horizon email verification code is: ${token}\n\nThis code expires in 24 hours.`);
  }

  async sendPasswordReset(email: string, token: string): Promise<void> {
    await this.deliver(email, "Reset your Horizon password", `Your Horizon password reset code is: ${token}\n\nThis code expires in 30 minutes. If you did not request it, ignore this message.`);
  }

  private async deliver(to: string, subject: string, text: string): Promise<void> {
    if (!this.transporter) {
      console.info(`[development email] to=${to} subject=${subject}\n${text}`);
      return;
    }
    await this.transporter.sendMail({ from: this.config.emailFrom, to, subject, text });
  }
}
