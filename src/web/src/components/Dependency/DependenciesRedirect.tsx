import { Navigate, useLocation } from "react-router-dom";
import { standaloneDependencyRedirect } from "./routes";

/** Compatibility for standalone dependency bookmarks; the settings tab is canonical. */
export default function DependenciesRedirect() {
  const location = useLocation();
  return <Navigate to={standaloneDependencyRedirect(location.search)} replace />;
}
