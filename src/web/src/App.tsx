import { useLocation } from "react-router-dom";
import { useEffect } from "react";
import { buildLogger } from "@/components/utils";
import { AppRouter } from "./router";

const log = buildLogger('App');

function App() {
  const location = useLocation();
  
  useEffect(() => {
    log('Current route:', location.pathname);
  }, [location]);

  return <AppRouter />;
}

export default App;
