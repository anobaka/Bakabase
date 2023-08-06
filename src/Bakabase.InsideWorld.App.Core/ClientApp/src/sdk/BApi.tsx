import { Message } from '@alifd/next';
import i18n from 'i18next';
import type { FullRequestParams, HttpResponse, ApiConfig } from '@/sdk/Api';
import { Api, ContentType } from '@/sdk/Api';
import serverConfig from '@/serverConfig';

class BApi extends Api<any> {
  constructor() {
    super({
      baseUrl: serverConfig.apiEndpoint,
    });
    const originalRequest = this.request;
    this.request = async <T = any, E = any>(params: FullRequestParams): Promise<T> => {
      try {
        const rsp = await originalRequest<T, E>(params);
        switch (rsp.code) {
          case 0:
            break;
          default:
            if ((rsp.code >= 400 || rsp.code < 200)) {
              Message.error({
                duration: 0,
                title: `${params.path}: [${rsp.code}]`,
                content: (
                  <pre>
                    {i18n.t(rsp.message)}
                  </pre>
                ),
                closeable: true,
              });
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
            duration: 0,
            title: `${params.path}: 请求异常，请稍后再试。`,
            content: (
              <pre>
                {error}
              </pre>
            ),
            closeable: true,
          });
        }

        throw error;
      }
    };
  }
}

export default new BApi();
