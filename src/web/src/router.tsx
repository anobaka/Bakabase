import { useRoutes, Navigate, RouteObject } from "react-router-dom";
import { routesMenuConfig, RouteMenuItem } from "@/components/routesMenuConfig";
import BasicLayout from "@/layouts/BasicLayout";
import BlankLayout from "@/layouts/BlankLayout";
import type { ReactNode } from "react";
import BakabaseContextProvider from "./components/ContextProvider/BakabaseContextProvider";

function getLayout(layout: string, children: ReactNode): ReactNode {
  switch (layout) {
    case "blank":
      return <BlankLayout>{children}</BlankLayout>;
    case "basic":
    default:
      return <BasicLayout>{children}</BasicLayout>;
  }
}

function flattenRoutes(config: RouteMenuItem[]): RouteObject[] {
  const result: RouteObject[] = [];
  for (const r of config) {
    if (r.children) {
      result.push(...flattenRoutes(r.children));
    }
    if (r.path && r.component) {
      result.push({
        path: r.path,
        element: getLayout(r.layout || "basic", <r.component />),
      });
    }
  }
  return result;
}

export default function AppRouter() {
  const routes = flattenRoutes(routesMenuConfig);
  routes.push({ path: "*", element: <Navigate to="/" replace /> });
  return (
    <BakabaseContextProvider>
      {useRoutes(routes)}
    </BakabaseContextProvider>
  )
}
