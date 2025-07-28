import type { RouteObject } from "react-router-dom";
import type { RouteMenuItem } from "@/components/routesMenuConfig";
import type { ReactNode } from "react";

import { useRoutes, Navigate, useLocation } from "react-router-dom";
import { useEffect, Suspense } from "react";

import BakabaseContextProvider from "./components/ContextProvider/BakabaseContextProvider";

import { routesMenuConfig } from "@/components/routesMenuConfig";
import BasicLayout from "@/layouts/BasicLayout";
import BlankLayout from "@/layouts/BlankLayout";
import { buildLogger } from "@/components/utils";

const log = buildLogger("App");

function getLayout(layout: string, children: ReactNode): ReactNode {
  switch (layout) {
    case "blank":
      return <BlankLayout>{children}</BlankLayout>;
    case "basic":
    default:
      return <BasicLayout>{children}</BasicLayout>;
  }
}

function isLazyComponent(component: React.ComponentType<any>): boolean {
  // Check if component is a lazy component by checking its $$typeof
  return (component as any).$$typeof === Symbol.for("react.lazy");
}

function flattenRoutes(config: RouteMenuItem[]): RouteObject[] {
  const result: RouteObject[] = [];

  for (const r of config) {
    if (r.children) {
      result.push(...flattenRoutes(r.children));
    }
    if (r.path && r.component) {
      const componentElement = <r.component />;

      result.push({
        path: r.path,
        element: getLayout(
          r.layout || "basic",
          isLazyComponent(r.component) ? (
            <Suspense
              fallback={
                <div className="flex items-center justify-center h-32">
                  Loading...
                </div>
              }
            >
              {componentElement}
            </Suspense>
          ) : (
            componentElement
          ),
        ),
      });
    }
  }

  return result;
}

export default function AppRouter() {
  const location = useLocation();

  useEffect(() => {
    log("Current route:", location.pathname);
  }, [location]);

  const routes = flattenRoutes(routesMenuConfig);

  routes.push({ path: "*", element: <Navigate replace to="/" /> });

  return <BakabaseContextProvider>{useRoutes(routes)}</BakabaseContextProvider>;
}
