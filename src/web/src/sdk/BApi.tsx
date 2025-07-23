import type { FullRequestParams } from "@/sdk/Api";

import { Api } from "@/sdk/Api";
import envConfig from "@/config/env";
import { buildLogger, extractErrorMessage } from "@/components/utils";
import { toast } from "@/components/bakaui";

interface BFullRequestParams extends FullRequestParams {
  ignoreError: (rsp) => boolean;
}

interface BResponse {
  code: number;
  message?: string;
}

const log = buildLogger("BApi");

export class BApi extends Api<any> {
  constructor(endpoint: string) {
    log("Initialize new instance with endpoint", endpoint);
    super({
      baseUrl: endpoint,
    });
    const originalRequest = this.request;

    this.request = async <T = any, E = any>(
      params: BFullRequestParams,
    ): Promise<T> => {
      try {
        log(params);
        const rsp = await originalRequest<T, E>(params);
        const typedRsp = rsp as BResponse;

        switch (typedRsp?.code) {
          case 0:
            break;
          default:
            if (params.ignoreError) {
              if (params.ignoreError(rsp)) {
                return rsp;
              }
            }
            if (typedRsp?.code >= 400 || typedRsp?.code < 200) {
              if (
                !params.ignoreError &&
                !typedRsp.message?.includes("SQLite Error")
              ) {
                const title = `[${typedRsp.code}]${params.path}`;

                toast.danger({
                  title,
                  description: typedRsp.message,
                });
              }
            }
        }

        return rsp;
      } catch (error) {
        log(error);

        if (!params.signal?.aborted) {
          const title = `${params.path}: 请求异常，请稍后再试。`;
          const description = extractErrorMessage(error);

          toast.danger({
            title,
            description,
          });
        }

        throw error;
      }
    };
  }
}

export default new BApi(envConfig.apiEndpoint);
