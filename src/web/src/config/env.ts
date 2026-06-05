interface EnvConfig {
  apiEndpoint: string;
}

export const envConfig: EnvConfig = {
  apiEndpoint: import.meta.env.VITE_API_ENDPOINT ?? "",
};

/**
 * Resolve a backend path to an absolute URL.
 *
 * `apiEndpoint` is empty in the packaged desktop build (the web UI and the API
 * are served from the same origin), so a bare path like `/download-task/xlsx`
 * stays relative. That is fine for in-page fetches, but breaks when the URL is
 * handed to `gui/openUrlInDefaultBrowser`: the backend feeds it to
 * `Process.Start`, which treats a relative path as an executable and throws
 * "The system cannot find the file specified". Falling back to the current
 * origin keeps the resulting URL absolute in every runtime mode.
 */
export const toAbsoluteBackendUrl = (path: string): string =>
  `${envConfig.apiEndpoint || window.location.origin}${path}`;

export default envConfig;
