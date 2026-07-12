import type { Metadata } from "next";
import { Inter } from "next/font/google";
import { ThemeProvider } from "flowbite-react";
import { Toaster } from "react-hot-toast";
import { ThemeInit } from "@/.flowbite-react/init";
import { auth } from "@/auth";
import { NotificationProvider } from "@/components/NotificationProvider";
import { customFlowbiteTheme } from "@/lib/flowbite-theme";
import "./globals.css";

const inter = Inter({
  variable: "--font-inter",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "ApexAutoBid",
  description: "Real-time online car auction platform",
};

export default async function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  // Server-side session read (Task 8.3) -- `NotificationProvider` (a Client Component) needs to
  // know, at mount, whether to open its SignalR connection with a token factory or purely
  // anonymously; it can't call `auth()` itself (server-only). Only `isSignedIn`/`username` cross
  // the server/client boundary here -- the raw access token never does (it's fetched separately,
  // browser-side, from the session-gated `GET /api/auth/hub-token` route -- see lib/signalr.ts).
  const session = await auth();
  const isSignedIn = Boolean(session?.user?.username);

  return (
    <html lang="en" className={`${inter.variable} h-full`}>
      <body className="min-h-full flex flex-col antialiased bg-slate-50 text-slate-700">
        <ThemeInit />
        <ThemeProvider theme={customFlowbiteTheme}>
          <NotificationProvider isSignedIn={isSignedIn} username={session?.user?.username}>
            {children}
          </NotificationProvider>
          {/* Mounted once, app-wide (Task 10, Docs/DesignGuide.md §6 -- "top-right,
              success/error variants"). Individual calls go through lib/toast.ts's
              helpers, which supply the per-variant (success/error/warning) colors;
              this only sets the shared base look and lifetime. */}
          <Toaster
            position="top-right"
            toastOptions={{
              duration: 4000,
              style: {
                borderRadius: "0.5rem",
                background: "#fff",
                color: "var(--color-slate-800)",
                border: "1px solid var(--color-slate-200)",
                boxShadow: "0 4px 6px -1px rgb(0 0 0 / 0.1)",
              },
            }}
          />
        </ThemeProvider>
      </body>
    </html>
  );
}
