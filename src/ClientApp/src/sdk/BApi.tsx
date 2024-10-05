import { Message } from '@alifd/next';
import i18n from 'i18next';
import type { FullRequestParams, HttpResponse, ApiConfig } from '@/sdk/Api';
import { Api, ContentType } from '@/sdk/Api';
import serverConfig from '@/serverConfig';

interface BFullRequestParams extends FullRequestParams {
  ignoreError: (rsp) => boolean;
}

interface BResponse {
  code: number;
  message?: string;
}

class BApi extends Api<any> {
  constructor() {
    super({
      baseUrl: serverConfig.apiEndpoint,
    });
    const originalRequest = this.request;
    this.request = async <T = any, E = any>(params: BFullRequestParams): Promise<T> => {
      try {
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
            if ((typedRsp?.code >= 400 || typedRsp?.code < 200)) {
              if (!params.ignoreError && !typedRsp.message?.includes('SQLite Error')) {
                Message.error({
                  duration: 5000,
                  title: `${params.path}: [${typedRsp.code}]`,
                  content: (
                    <pre>
                      {/* @ts-ignore */}
                      {i18n.t(typedRsp.message)}
                    </pre>
                  ),
                  closeable: true,
                  hasMask: true,
                });
              }
            }
        }
        return rsp;
      } catch (error) {
        // switch (error.code) {
        //   case 'ERR_CANCELED': {
        //     return;
        //   }
        // }

        if (!params.signal?.aborted) {
          Message.error({
            duration: 5000,
            title: `${params.path}: 请求异常，请稍后再试。`,
            content: (
              <pre>
                {error}
              </pre>
            ),
            closeable: true,
            hasMask: true,
          });
        }

        throw error;
      }
    };
  }
}

export default new BApi();
