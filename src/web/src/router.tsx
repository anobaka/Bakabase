import { Route, Routes, Navigate } from "react-router-dom";
import { buildLogger } from "@/components/utils";
import BasicLayout from "@/layouts/BasicLayout";
import BlankLayout from "@/layouts/BlankLayout";
import BakabaseContextProvider from "@/components/ContextProvider/BakabaseContextProvider";

const log = buildLogger('Router');

// Import all pages dynamically - support both index.tsx and page.tsx
const pages = import.meta.glob('./pages/**/*.tsx', { eager: true });

interface RouteConfig {
  path: string;
  component: any;
  layout?: 'basic' | 'blank' | 'none';
}

// Create route mapping based on file structure
const createRoutes = (): RouteConfig[] => {
  const routes: RouteConfig[] = [];
  const processedPaths = new Set<string>(); // Track processed paths to avoid duplicates
  
  Object.entries(pages).forEach(([path, module]) => {
    // Skip layout files
    if (path.includes('layout.tsx')) {
      return;
    }
    
    // Extract route path from file path
    let routePath = path
      .replace('./pages/', '')
      .replace('/index.tsx', '')
      .toLowerCase();
    
    // Skip if we've already processed this route (index.tsx takes priority)
    if (processedPaths.has(routePath)) {
      return;
    }
    
    // Handle root page
    if (routePath === 'page') {
      routes.push({ 
        path: '/', 
        component: (module as any).default,
        layout: 'basic'
      });
      processedPaths.add('/');
    } else {
      // Determine layout based on route
      const layout = routePath === 'welcome' ? 'blank' : 'basic';
      routes.push({ 
        path: `/${routePath}`, 
        component: (module as any).default,
        layout
      });
      processedPaths.add(routePath);
    }
  });
  
  return routes;
};

// Layout wrapper component
const LayoutWrapper = ({ 
  children, 
  layout = 'basic' 
}: { 
  children: React.ReactNode; 
  layout?: 'basic' | 'blank' | 'none';
}) => {
  switch (layout) {
    case 'blank':
      return <BlankLayout>{children}</BlankLayout>;
    case 'basic':
      return <BasicLayout>{children}</BasicLayout>;
    case 'none':
      return <>{children}</>;
    default:
      return <BasicLayout>{children}</BasicLayout>;
  }
};

// Main router component
export function AppRouter() {
  const routes = createRoutes();
  
  log('Generated routes:', routes.map(r => ({ path: r.path, layout: r.layout })));

  return (
    <BakabaseContextProvider>
      <Routes>
        {routes.map(({ path, component: Component, layout }) => (
          <Route 
            key={path} 
            path={path} 
            element={
              <LayoutWrapper layout={layout}>
                <Component />
              </LayoutWrapper>
            } 
          />
        ))}
        {/* Fallback route */}
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BakabaseContextProvider>
  );
} 