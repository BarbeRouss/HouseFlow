import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "HouseFlow",
  description: "Manage your house maintenance and devices",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return children;
}
