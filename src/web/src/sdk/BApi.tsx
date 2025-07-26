import { Api } from "@/sdk/Api";
import envConfig from "@/config/env";

const BApi = new Api({ baseUrl: envConfig.apiEndpoint });

export default BApi;
