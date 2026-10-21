import { Api } from "@/sdk/Api";
import envConfig from "@/config/env";
import { analyticsRequestHeaders } from "@/services/analyticsContext";

const BApi = new Api({
  baseUrl: envConfig.apiEndpoint,
  customFetch: (input, init) => {
    const headers = new Headers(init?.headers);
    for (const [name, value] of Object.entries(
      analyticsRequestHeaders(input, envConfig.apiEndpoint),
    )) {
      headers.set(name, value);
    }
    return fetch(input, { ...init, headers });
  },
});

export default BApi;
