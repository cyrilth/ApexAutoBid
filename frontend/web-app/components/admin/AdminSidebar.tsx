"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { Sidebar, SidebarItem, SidebarItemGroup, SidebarItems } from "flowbite-react";

const NAV_ITEMS = [
  { href: "/admin", label: "Dashboard" },
  { href: "/admin/users", label: "Users" },
  { href: "/admin/auctions", label: "Auctions" },
  { href: "/admin/banners", label: "Banners" },
  { href: "/admin/settings", label: "Settings" },
] as const;

/**
 * Admin area's sidebar nav (Docs/DesignGuide.md §4 -- "Flowbite `Sidebar` layout (Dashboard,
 * Users, Auctions, Banners, Settings)"). A Client Component so it can highlight the active
 * section via `usePathname` -- `app/admin/layout.tsx` (a Server Component) renders this once
 * and reuses it across every `/admin/*` page.
 */
export function AdminSidebar() {
  const pathname = usePathname();

  return (
    <Sidebar aria-label="Admin navigation" className="h-full">
      <SidebarItems>
        <SidebarItemGroup>
          {NAV_ITEMS.map((item) => (
            <SidebarItem
              key={item.href}
              as={Link}
              href={item.href}
              active={item.href === "/admin" ? pathname === "/admin" : pathname.startsWith(item.href)}
            >
              {item.label}
            </SidebarItem>
          ))}
        </SidebarItemGroup>
        <SidebarItemGroup>
          <SidebarItem as={Link} href="/">
            &larr; Back to site
          </SidebarItem>
        </SidebarItemGroup>
      </SidebarItems>
    </Sidebar>
  );
}
