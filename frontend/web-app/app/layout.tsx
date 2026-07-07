import type { Metadata } from "next";
import { Inter } from "next/font/google";
import { ThemeInit } from "@/.flowbite-react/init";
import "./globals.css";

const inter = Inter({
  variable: "--font-inter",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "ApexAutoBid",
  description: "Real-time online car auction platform",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en" className={`${inter.variable} h-full`}>
      <body className="min-h-full flex flex-col antialiased bg-slate-50 text-slate-700">
        <ThemeInit />
        {children}
      </body>
    </html>
  );
}
