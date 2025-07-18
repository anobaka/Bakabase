import ReactDOM from "react-dom/client";
import { HashRouter } from "react-router-dom";

import "@/styles/globals.css";

import AppRouter from "./router";

ReactDOM.createRoot(document.getElementById("root")!).render(
  // <React.StrictMode>
    <HashRouter>
      <AppRouter />
    </HashRouter>
  // </React.StrictMode>,
);
