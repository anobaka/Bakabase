/* eslint-disable */
let domain = ''
export const getDomain = () => {
  return domain
}
export const setDomain = ($domain) => {
  domain = $domain
}
import request from './api-wrapper';
/*==========================================================
 *                    
 ==========================================================*/
/**
 * 
 * request: SearchAliasGroups
 * url: SearchAliasGroupsURL
 * method: SearchAliasGroups_TYPE
 * raw_url: SearchAliasGroups_RAW_URL
 * @param texts - 
 * @param text - 
 * @param fuzzyText - 
 * @param pageIndex - 
 * @param pageSize - 
 * @param skipCount - 
 */
export const SearchAliasGroups = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/alias'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['texts'] !== undefined) {
    queryParameters['texts'] = parameters['texts']
  }
  if (parameters['text'] !== undefined) {
    queryParameters['text'] = parameters['text']
  }
  if (parameters['fuzzyText'] !== undefined) {
    queryParameters['fuzzyText'] = parameters['fuzzyText']
  }
  if (parameters['pageIndex'] !== undefined) {
    queryParameters['pageIndex'] = parameters['pageIndex']
  }
  if (parameters['pageSize'] !== undefined) {
    queryParameters['pageSize'] = parameters['pageSize']
  }
  if (parameters['skipCount'] !== undefined) {
    queryParameters['skipCount'] = parameters['skipCount']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const SearchAliasGroups_RAW_URL = function() {
  return '/alias'
}
export const SearchAliasGroups_TYPE = function() {
  return 'get'
}
export const SearchAliasGroupsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/alias'
  if (parameters['texts'] !== undefined) {
    queryParameters['texts'] = parameters['texts']
  }
  if (parameters['text'] !== undefined) {
    queryParameters['text'] = parameters['text']
  }
  if (parameters['fuzzyText'] !== undefined) {
    queryParameters['fuzzyText'] = parameters['fuzzyText']
  }
  if (parameters['pageIndex'] !== undefined) {
    queryParameters['pageIndex'] = parameters['pageIndex']
  }
  if (parameters['pageSize'] !== undefined) {
    queryParameters['pageSize'] = parameters['pageSize']
  }
  if (parameters['skipCount'] !== undefined) {
    queryParameters['skipCount'] = parameters['skipCount']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchAlias
 * url: PatchAliasURL
 * method: PatchAlias_TYPE
 * raw_url: PatchAlias_RAW_URL
 * @param text - 
 * @param model - 
 */
export const PatchAlias = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/alias'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['text'] !== undefined) {
    queryParameters['text'] = parameters['text']
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const PatchAlias_RAW_URL = function() {
  return '/alias'
}
export const PatchAlias_TYPE = function() {
  return 'put'
}
export const PatchAliasURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/alias'
  if (parameters['text'] !== undefined) {
    queryParameters['text'] = parameters['text']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: AddAlias
 * url: AddAliasURL
 * method: AddAlias_TYPE
 * raw_url: AddAlias_RAW_URL
 * @param model - 
 */
export const AddAlias = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/alias'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const AddAlias_RAW_URL = function() {
  return '/alias'
}
export const AddAlias_TYPE = function() {
  return 'post'
}
export const AddAliasURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/alias'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: DeleteAlias
 * url: DeleteAliasURL
 * method: DeleteAlias_TYPE
 * raw_url: DeleteAlias_RAW_URL
 * @param text - 
 */
export const DeleteAlias = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/alias'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['text'] !== undefined) {
    queryParameters['text'] = parameters['text']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const DeleteAlias_RAW_URL = function() {
  return '/alias'
}
export const DeleteAlias_TYPE = function() {
  return 'delete'
}
export const DeleteAliasURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/alias'
  if (parameters['text'] !== undefined) {
    queryParameters['text'] = parameters['text']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: DeleteAliasGroups
 * url: DeleteAliasGroupsURL
 * method: DeleteAliasGroups_TYPE
 * raw_url: DeleteAliasGroups_RAW_URL
 * @param preferredTexts - 
 */
export const DeleteAliasGroups = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/alias/groups'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['preferredTexts'] !== undefined) {
    queryParameters['preferredTexts'] = parameters['preferredTexts']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const DeleteAliasGroups_RAW_URL = function() {
  return '/alias/groups'
}
export const DeleteAliasGroups_TYPE = function() {
  return 'delete'
}
export const DeleteAliasGroupsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/alias/groups'
  if (parameters['preferredTexts'] !== undefined) {
    queryParameters['preferredTexts'] = parameters['preferredTexts']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: MergeAliasGroups
 * url: MergeAliasGroupsURL
 * method: MergeAliasGroups_TYPE
 * raw_url: MergeAliasGroups_RAW_URL
 * @param preferredTexts - 
 */
export const MergeAliasGroups = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/alias/merge'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['preferredTexts'] !== undefined) {
    queryParameters['preferredTexts'] = parameters['preferredTexts']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const MergeAliasGroups_RAW_URL = function() {
  return '/alias/merge'
}
export const MergeAliasGroups_TYPE = function() {
  return 'put'
}
export const MergeAliasGroupsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/alias/merge'
  if (parameters['preferredTexts'] !== undefined) {
    queryParameters['preferredTexts'] = parameters['preferredTexts']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: CheckAppInitialized
 * url: CheckAppInitializedURL
 * method: CheckAppInitialized_TYPE
 * raw_url: CheckAppInitialized_RAW_URL
 */
export const CheckAppInitialized = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/app/initialized'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const CheckAppInitialized_RAW_URL = function() {
  return '/app/initialized'
}
export const CheckAppInitialized_TYPE = function() {
  return 'get'
}
export const CheckAppInitializedURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/app/initialized'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAppInfo
 * url: GetAppInfoURL
 * method: GetAppInfo_TYPE
 * raw_url: GetAppInfo_RAW_URL
 */
export const GetAppInfo = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/app/info'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAppInfo_RAW_URL = function() {
  return '/app/info'
}
export const GetAppInfo_TYPE = function() {
  return 'get'
}
export const GetAppInfoURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/app/info'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: AcceptTerms
 * url: AcceptTermsURL
 * method: AcceptTerms_TYPE
 * raw_url: AcceptTerms_RAW_URL
 */
export const AcceptTerms = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/app/terms'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const AcceptTerms_RAW_URL = function() {
  return '/app/terms'
}
export const AcceptTerms_TYPE = function() {
  return 'post'
}
export const AcceptTermsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/app/terms'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: MoveCoreData
 * url: MoveCoreDataURL
 * method: MoveCoreData_TYPE
 * raw_url: MoveCoreData_RAW_URL
 * @param model - 
 */
export const MoveCoreData = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/app/data-path'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const MoveCoreData_RAW_URL = function() {
  return '/app/data-path'
}
export const MoveCoreData_TYPE = function() {
  return 'put'
}
export const MoveCoreDataURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/app/data-path'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllBackgroundTasks
 * url: GetAllBackgroundTasksURL
 * method: GetAllBackgroundTasks_TYPE
 * raw_url: GetAllBackgroundTasks_RAW_URL
 */
export const GetAllBackgroundTasks = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/background-task'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllBackgroundTasks_RAW_URL = function() {
  return '/background-task'
}
export const GetAllBackgroundTasks_TYPE = function() {
  return 'get'
}
export const GetAllBackgroundTasksURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/background-task'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: ClearInactiveBackgroundTasks
 * url: ClearInactiveBackgroundTasksURL
 * method: ClearInactiveBackgroundTasks_TYPE
 * raw_url: ClearInactiveBackgroundTasks_RAW_URL
 */
export const ClearInactiveBackgroundTasks = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/background-task'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const ClearInactiveBackgroundTasks_RAW_URL = function() {
  return '/background-task'
}
export const ClearInactiveBackgroundTasks_TYPE = function() {
  return 'delete'
}
export const ClearInactiveBackgroundTasksURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/background-task'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetBackgroundTaskByName
 * url: GetBackgroundTaskByNameURL
 * method: GetBackgroundTaskByName_TYPE
 * raw_url: GetBackgroundTaskByName_RAW_URL
 * @param name - 
 */
export const GetBackgroundTaskByName = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/background-task/by-name'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['name'] !== undefined) {
    queryParameters['name'] = parameters['name']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetBackgroundTaskByName_RAW_URL = function() {
  return '/background-task/by-name'
}
export const GetBackgroundTaskByName_TYPE = function() {
  return 'get'
}
export const GetBackgroundTaskByNameURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/background-task/by-name'
  if (parameters['name'] !== undefined) {
    queryParameters['name'] = parameters['name']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StopBackgroundTask
 * url: StopBackgroundTaskURL
 * method: StopBackgroundTask_TYPE
 * raw_url: StopBackgroundTask_RAW_URL
 * @param id - 
 */
export const StopBackgroundTask = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/background-task/{id}/stop'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const StopBackgroundTask_RAW_URL = function() {
  return '/background-task/{id}/stop'
}
export const StopBackgroundTask_TYPE = function() {
  return 'delete'
}
export const StopBackgroundTaskURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/background-task/{id}/stop'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveBackgroundTask
 * url: RemoveBackgroundTaskURL
 * method: RemoveBackgroundTask_TYPE
 * raw_url: RemoveBackgroundTask_RAW_URL
 * @param id - 
 */
export const RemoveBackgroundTask = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/background-task/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveBackgroundTask_RAW_URL = function() {
  return '/background-task/{id}'
}
export const RemoveBackgroundTask_TYPE = function() {
  return 'delete'
}
export const RemoveBackgroundTaskURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/background-task/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetBiliBiliFavorites
 * url: GetBiliBiliFavoritesURL
 * method: GetBiliBiliFavorites_TYPE
 * raw_url: GetBiliBiliFavorites_RAW_URL
 */
export const GetBiliBiliFavorites = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/bilibili/favorites'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetBiliBiliFavorites_RAW_URL = function() {
  return '/bilibili/favorites'
}
export const GetBiliBiliFavorites_TYPE = function() {
  return 'get'
}
export const GetBiliBiliFavoritesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/bilibili/favorites'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetBulkModificationById
 * url: GetBulkModificationByIdURL
 * method: GetBulkModificationById_TYPE
 * raw_url: GetBulkModificationById_RAW_URL
 * @param id - 
 */
export const GetBulkModificationById = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/bulk-modification/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetBulkModificationById_RAW_URL = function() {
  return '/bulk-modification/{id}'
}
export const GetBulkModificationById_TYPE = function() {
  return 'get'
}
export const GetBulkModificationByIdURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/bulk-modification/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PutBulkModification
 * url: PutBulkModificationURL
 * method: PutBulkModification_TYPE
 * raw_url: PutBulkModification_RAW_URL
 * @param id - 
 * @param model - 
 */
export const PutBulkModification = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/bulk-modification/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const PutBulkModification_RAW_URL = function() {
  return '/bulk-modification/{id}'
}
export const PutBulkModification_TYPE = function() {
  return 'put'
}
export const PutBulkModificationURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/bulk-modification/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: DeleteBulkModification
 * url: DeleteBulkModificationURL
 * method: DeleteBulkModification_TYPE
 * raw_url: DeleteBulkModification_RAW_URL
 * @param id - 
 */
export const DeleteBulkModification = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/bulk-modification/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const DeleteBulkModification_RAW_URL = function() {
  return '/bulk-modification/{id}'
}
export const DeleteBulkModification_TYPE = function() {
  return 'delete'
}
export const DeleteBulkModificationURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/bulk-modification/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllBulkModifications
 * url: GetAllBulkModificationsURL
 * method: GetAllBulkModifications_TYPE
 * raw_url: GetAllBulkModifications_RAW_URL
 */
export const GetAllBulkModifications = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/bulk-modification'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllBulkModifications_RAW_URL = function() {
  return '/bulk-modification'
}
export const GetAllBulkModifications_TYPE = function() {
  return 'get'
}
export const GetAllBulkModificationsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/bulk-modification'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: CreateBulkModification
 * url: CreateBulkModificationURL
 * method: CreateBulkModification_TYPE
 * raw_url: CreateBulkModification_RAW_URL
 * @param model - 
 */
export const CreateBulkModification = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/bulk-modification'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const CreateBulkModification_RAW_URL = function() {
  return '/bulk-modification'
}
export const CreateBulkModification_TYPE = function() {
  return 'post'
}
export const CreateBulkModificationURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/bulk-modification'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: DuplicateBulkModification
 * url: DuplicateBulkModificationURL
 * method: DuplicateBulkModification_TYPE
 * raw_url: DuplicateBulkModification_RAW_URL
 * @param id - 
 */
export const DuplicateBulkModification = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/bulk-modification/{id}/duplication'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const DuplicateBulkModification_RAW_URL = function() {
  return '/bulk-modification/{id}/duplication'
}
export const DuplicateBulkModification_TYPE = function() {
  return 'post'
}
export const DuplicateBulkModificationURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/bulk-modification/{id}/duplication'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: CloseBulkModification
 * url: CloseBulkModificationURL
 * method: CloseBulkModification_TYPE
 * raw_url: CloseBulkModification_RAW_URL
 * @param id - 
 */
export const CloseBulkModification = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/bulk-modification/{id}/close'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const CloseBulkModification_RAW_URL = function() {
  return '/bulk-modification/{id}/close'
}
export const CloseBulkModification_TYPE = function() {
  return 'put'
}
export const CloseBulkModificationURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/bulk-modification/{id}/close'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PerformBulkModificationFiltering
 * url: PerformBulkModificationFilteringURL
 * method: PerformBulkModificationFiltering_TYPE
 * raw_url: PerformBulkModificationFiltering_RAW_URL
 * @param id - 
 */
export const PerformBulkModificationFiltering = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/bulk-modification/{id}/filter'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const PerformBulkModificationFiltering_RAW_URL = function() {
  return '/bulk-modification/{id}/filter'
}
export const PerformBulkModificationFiltering_TYPE = function() {
  return 'put'
}
export const PerformBulkModificationFilteringURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/bulk-modification/{id}/filter'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetBulkModificationFilteredResources
 * url: GetBulkModificationFilteredResourcesURL
 * method: GetBulkModificationFilteredResources_TYPE
 * raw_url: GetBulkModificationFilteredResources_RAW_URL
 * @param id - 
 */
export const GetBulkModificationFilteredResources = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/bulk-modification/{id}/filtered-resources'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetBulkModificationFilteredResources_RAW_URL = function() {
  return '/bulk-modification/{id}/filtered-resources'
}
export const GetBulkModificationFilteredResources_TYPE = function() {
  return 'get'
}
export const GetBulkModificationFilteredResourcesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/bulk-modification/{id}/filtered-resources'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetBulkModificationResourceDiffs
 * url: GetBulkModificationResourceDiffsURL
 * method: GetBulkModificationResourceDiffs_TYPE
 * raw_url: GetBulkModificationResourceDiffs_RAW_URL
 * @param bmId - 
 */
export const GetBulkModificationResourceDiffs = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/bulk-modification/{bmId}/diffs'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{bmId}', `${parameters['bmId']}`)
  if (parameters['bmId'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: bmId'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetBulkModificationResourceDiffs_RAW_URL = function() {
  return '/bulk-modification/{bmId}/diffs'
}
export const GetBulkModificationResourceDiffs_TYPE = function() {
  return 'get'
}
export const GetBulkModificationResourceDiffsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/bulk-modification/{bmId}/diffs'
  path = path.replace('{bmId}', `${parameters['bmId']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: CalculateBulkModificationResourceDiffs
 * url: CalculateBulkModificationResourceDiffsURL
 * method: CalculateBulkModificationResourceDiffs_TYPE
 * raw_url: CalculateBulkModificationResourceDiffs_RAW_URL
 * @param id - 
 */
export const CalculateBulkModificationResourceDiffs = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/bulk-modification/{id}/diffs'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const CalculateBulkModificationResourceDiffs_RAW_URL = function() {
  return '/bulk-modification/{id}/diffs'
}
export const CalculateBulkModificationResourceDiffs_TYPE = function() {
  return 'post'
}
export const CalculateBulkModificationResourceDiffsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/bulk-modification/{id}/diffs'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: ApplyBulkModification
 * url: ApplyBulkModificationURL
 * method: ApplyBulkModification_TYPE
 * raw_url: ApplyBulkModification_RAW_URL
 * @param id - 
 */
export const ApplyBulkModification = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/bulk-modification/{id}/apply'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const ApplyBulkModification_RAW_URL = function() {
  return '/bulk-modification/{id}/apply'
}
export const ApplyBulkModification_TYPE = function() {
  return 'post'
}
export const ApplyBulkModificationURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/bulk-modification/{id}/apply'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RevertBulkModification
 * url: RevertBulkModificationURL
 * method: RevertBulkModification_TYPE
 * raw_url: RevertBulkModification_RAW_URL
 * @param id - 
 */
export const RevertBulkModification = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/bulk-modification/{id}/revert'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const RevertBulkModification_RAW_URL = function() {
  return '/bulk-modification/{id}/revert'
}
export const RevertBulkModification_TYPE = function() {
  return 'post'
}
export const RevertBulkModificationURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/bulk-modification/{id}/revert'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetCategory
 * url: GetCategoryURL
 * method: GetCategory_TYPE
 * raw_url: GetCategory_RAW_URL
 * @param id - 
 * @param additionalItems - 
 */
export const GetCategory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/category/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetCategory_RAW_URL = function() {
  return '/category/{id}'
}
export const GetCategory_TYPE = function() {
  return 'get'
}
export const GetCategoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/category/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchCategory
 * url: PatchCategoryURL
 * method: PatchCategory_TYPE
 * raw_url: PatchCategory_RAW_URL
 * @param id - 
 * @param model - 
 */
export const PatchCategory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/category/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const PatchCategory_RAW_URL = function() {
  return '/category/{id}'
}
export const PatchCategory_TYPE = function() {
  return 'put'
}
export const PatchCategoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/category/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: DeleteCategory
 * url: DeleteCategoryURL
 * method: DeleteCategory_TYPE
 * raw_url: DeleteCategory_RAW_URL
 * @param id - 
 */
export const DeleteCategory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/category/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const DeleteCategory_RAW_URL = function() {
  return '/category/{id}'
}
export const DeleteCategory_TYPE = function() {
  return 'delete'
}
export const DeleteCategoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/category/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllCategories
 * url: GetAllCategoriesURL
 * method: GetAllCategories_TYPE
 * raw_url: GetAllCategories_RAW_URL
 * @param additionalItems - 
 */
export const GetAllCategories = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/category'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllCategories_RAW_URL = function() {
  return '/category'
}
export const GetAllCategories_TYPE = function() {
  return 'get'
}
export const GetAllCategoriesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/category'
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: AddCategory
 * url: AddCategoryURL
 * method: AddCategory_TYPE
 * raw_url: AddCategory_RAW_URL
 * @param model - 
 */
export const AddCategory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/category'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const AddCategory_RAW_URL = function() {
  return '/category'
}
export const AddCategory_TYPE = function() {
  return 'post'
}
export const AddCategoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/category'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: DuplicateCategory
 * url: DuplicateCategoryURL
 * method: DuplicateCategory_TYPE
 * raw_url: DuplicateCategory_RAW_URL
 * @param id - 
 * @param model - 
 */
export const DuplicateCategory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/category/{id}/duplication'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const DuplicateCategory_RAW_URL = function() {
  return '/category/{id}/duplication'
}
export const DuplicateCategory_TYPE = function() {
  return 'post'
}
export const DuplicateCategoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/category/{id}/duplication'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: ConfigureCategoryComponents
 * url: ConfigureCategoryComponentsURL
 * method: ConfigureCategoryComponents_TYPE
 * raw_url: ConfigureCategoryComponents_RAW_URL
 * @param id - 
 * @param model - 
 */
export const ConfigureCategoryComponents = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/category/{id}/component'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const ConfigureCategoryComponents_RAW_URL = function() {
  return '/category/{id}/component'
}
export const ConfigureCategoryComponents_TYPE = function() {
  return 'put'
}
export const ConfigureCategoryComponentsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/category/{id}/component'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: SortCategories
 * url: SortCategoriesURL
 * method: SortCategories_TYPE
 * raw_url: SortCategories_RAW_URL
 * @param model - 
 */
export const SortCategories = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/category/orders'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const SortCategories_RAW_URL = function() {
  return '/category/orders'
}
export const SortCategories_TYPE = function() {
  return 'put'
}
export const SortCategoriesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/category/orders'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: BindCustomPropertiesToCategory
 * url: BindCustomPropertiesToCategoryURL
 * method: BindCustomPropertiesToCategory_TYPE
 * raw_url: BindCustomPropertiesToCategory_RAW_URL
 * @param id - 
 * @param model - 
 */
export const BindCustomPropertiesToCategory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/category/{id}/custom-properties'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const BindCustomPropertiesToCategory_RAW_URL = function() {
  return '/category/{id}/custom-properties'
}
export const BindCustomPropertiesToCategory_TYPE = function() {
  return 'put'
}
export const BindCustomPropertiesToCategoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/category/{id}/custom-properties'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: BindCustomPropertyToCategory
 * url: BindCustomPropertyToCategoryURL
 * method: BindCustomPropertyToCategory_TYPE
 * raw_url: BindCustomPropertyToCategory_RAW_URL
 * @param categoryId - 
 * @param customPropertyId - 
 */
export const BindCustomPropertyToCategory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/category/{categoryId}/custom-property/{customPropertyId}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{categoryId}', `${parameters['categoryId']}`)
  if (parameters['categoryId'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: categoryId'))
  }
  path = path.replace('{customPropertyId}', `${parameters['customPropertyId']}`)
  if (parameters['customPropertyId'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: customPropertyId'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const BindCustomPropertyToCategory_RAW_URL = function() {
  return '/category/{categoryId}/custom-property/{customPropertyId}'
}
export const BindCustomPropertyToCategory_TYPE = function() {
  return 'post'
}
export const BindCustomPropertyToCategoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/category/{categoryId}/custom-property/{customPropertyId}'
  path = path.replace('{categoryId}', `${parameters['categoryId']}`)
  path = path.replace('{customPropertyId}', `${parameters['customPropertyId']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PreviewCategoryDisplayNameTemplate
 * url: PreviewCategoryDisplayNameTemplateURL
 * method: PreviewCategoryDisplayNameTemplate_TYPE
 * raw_url: PreviewCategoryDisplayNameTemplate_RAW_URL
 * @param id - 
 * @param template - 
 * @param maxCount - 
 */
export const PreviewCategoryDisplayNameTemplate = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/category/{id}/resource/resource-display-name-template/preview'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['template'] !== undefined) {
    queryParameters['template'] = parameters['template']
  }
  if (parameters['maxCount'] !== undefined) {
    queryParameters['maxCount'] = parameters['maxCount']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const PreviewCategoryDisplayNameTemplate_RAW_URL = function() {
  return '/category/{id}/resource/resource-display-name-template/preview'
}
export const PreviewCategoryDisplayNameTemplate_TYPE = function() {
  return 'get'
}
export const PreviewCategoryDisplayNameTemplateURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/category/{id}/resource/resource-display-name-template/preview'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['template'] !== undefined) {
    queryParameters['template'] = parameters['template']
  }
  if (parameters['maxCount'] !== undefined) {
    queryParameters['maxCount'] = parameters['maxCount']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetCategoryEnhancerOptions
 * url: GetCategoryEnhancerOptionsURL
 * method: GetCategoryEnhancerOptions_TYPE
 * raw_url: GetCategoryEnhancerOptions_RAW_URL
 * @param id - 
 * @param enhancerId - 
 */
export const GetCategoryEnhancerOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/category/{id}/enhancer/{enhancerId}/options'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  path = path.replace('{enhancerId}', `${parameters['enhancerId']}`)
  if (parameters['enhancerId'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: enhancerId'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetCategoryEnhancerOptions_RAW_URL = function() {
  return '/category/{id}/enhancer/{enhancerId}/options'
}
export const GetCategoryEnhancerOptions_TYPE = function() {
  return 'get'
}
export const GetCategoryEnhancerOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/category/{id}/enhancer/{enhancerId}/options'
  path = path.replace('{id}', `${parameters['id']}`)
  path = path.replace('{enhancerId}', `${parameters['enhancerId']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchCategoryEnhancerOptions
 * url: PatchCategoryEnhancerOptionsURL
 * method: PatchCategoryEnhancerOptions_TYPE
 * raw_url: PatchCategoryEnhancerOptions_RAW_URL
 * @param id - 
 * @param enhancerId - 
 * @param model - 
 */
export const PatchCategoryEnhancerOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/category/{id}/enhancer/{enhancerId}/options'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  path = path.replace('{enhancerId}', `${parameters['enhancerId']}`)
  if (parameters['enhancerId'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: enhancerId'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const PatchCategoryEnhancerOptions_RAW_URL = function() {
  return '/category/{id}/enhancer/{enhancerId}/options'
}
export const PatchCategoryEnhancerOptions_TYPE = function() {
  return 'patch'
}
export const PatchCategoryEnhancerOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/category/{id}/enhancer/{enhancerId}/options'
  path = path.replace('{id}', `${parameters['id']}`)
  path = path.replace('{enhancerId}', `${parameters['enhancerId']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: DeleteCategoryEnhancerTargetOptions
 * url: DeleteCategoryEnhancerTargetOptionsURL
 * method: DeleteCategoryEnhancerTargetOptions_TYPE
 * raw_url: DeleteCategoryEnhancerTargetOptions_RAW_URL
 * @param id - 
 * @param enhancerId - 
 * @param target - 
 * @param dynamicTarget - 
 */
export const DeleteCategoryEnhancerTargetOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/category/{id}/enhancer/{enhancerId}/options/target'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  path = path.replace('{enhancerId}', `${parameters['enhancerId']}`)
  if (parameters['enhancerId'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: enhancerId'))
  }
  if (parameters['target'] !== undefined) {
    queryParameters['target'] = parameters['target']
  }
  if (parameters['dynamicTarget'] !== undefined) {
    queryParameters['dynamicTarget'] = parameters['dynamicTarget']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const DeleteCategoryEnhancerTargetOptions_RAW_URL = function() {
  return '/category/{id}/enhancer/{enhancerId}/options/target'
}
export const DeleteCategoryEnhancerTargetOptions_TYPE = function() {
  return 'delete'
}
export const DeleteCategoryEnhancerTargetOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/category/{id}/enhancer/{enhancerId}/options/target'
  path = path.replace('{id}', `${parameters['id']}`)
  path = path.replace('{enhancerId}', `${parameters['enhancerId']}`)
  if (parameters['target'] !== undefined) {
    queryParameters['target'] = parameters['target']
  }
  if (parameters['dynamicTarget'] !== undefined) {
    queryParameters['dynamicTarget'] = parameters['dynamicTarget']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchCategoryEnhancerTargetOptions
 * url: PatchCategoryEnhancerTargetOptionsURL
 * method: PatchCategoryEnhancerTargetOptions_TYPE
 * raw_url: PatchCategoryEnhancerTargetOptions_RAW_URL
 * @param id - 
 * @param enhancerId - 
 * @param target - 
 * @param dynamicTarget - 
 * @param model - 
 */
export const PatchCategoryEnhancerTargetOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/category/{id}/enhancer/{enhancerId}/options/target'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  path = path.replace('{enhancerId}', `${parameters['enhancerId']}`)
  if (parameters['enhancerId'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: enhancerId'))
  }
  if (parameters['target'] !== undefined) {
    queryParameters['target'] = parameters['target']
  }
  if (parameters['target'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: target'))
  }
  if (parameters['dynamicTarget'] !== undefined) {
    queryParameters['dynamicTarget'] = parameters['dynamicTarget']
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const PatchCategoryEnhancerTargetOptions_RAW_URL = function() {
  return '/category/{id}/enhancer/{enhancerId}/options/target'
}
export const PatchCategoryEnhancerTargetOptions_TYPE = function() {
  return 'patch'
}
export const PatchCategoryEnhancerTargetOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/category/{id}/enhancer/{enhancerId}/options/target'
  path = path.replace('{id}', `${parameters['id']}`)
  path = path.replace('{enhancerId}', `${parameters['enhancerId']}`)
  if (parameters['target'] !== undefined) {
    queryParameters['target'] = parameters['target']
  }
  if (parameters['dynamicTarget'] !== undefined) {
    queryParameters['dynamicTarget'] = parameters['dynamicTarget']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StartSyncingCategoryResources
 * url: StartSyncingCategoryResourcesURL
 * method: StartSyncingCategoryResources_TYPE
 * raw_url: StartSyncingCategoryResources_RAW_URL
 * @param id - 
 */
export const StartSyncingCategoryResources = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/category/{id}/synchronization'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const StartSyncingCategoryResources_RAW_URL = function() {
  return '/category/{id}/synchronization'
}
export const StartSyncingCategoryResources_TYPE = function() {
  return 'put'
}
export const StartSyncingCategoryResourcesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/category/{id}/synchronization'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetComponentDescriptors
 * url: GetComponentDescriptorsURL
 * method: GetComponentDescriptors_TYPE
 * raw_url: GetComponentDescriptors_RAW_URL
 * @param type - 
 * @param additionalItems - 
 */
export const GetComponentDescriptors = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/component'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['type'] !== undefined) {
    queryParameters['type'] = parameters['type']
  }
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetComponentDescriptors_RAW_URL = function() {
  return '/component'
}
export const GetComponentDescriptors_TYPE = function() {
  return 'get'
}
export const GetComponentDescriptorsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/component'
  if (parameters['type'] !== undefined) {
    queryParameters['type'] = parameters['type']
  }
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetComponentDescriptorByKey
 * url: GetComponentDescriptorByKeyURL
 * method: GetComponentDescriptorByKey_TYPE
 * raw_url: GetComponentDescriptorByKey_RAW_URL
 * @param key - 
 * @param additionalItems - 
 */
export const GetComponentDescriptorByKey = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/component/key'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['key'] !== undefined) {
    queryParameters['key'] = parameters['key']
  }
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetComponentDescriptorByKey_RAW_URL = function() {
  return '/component/key'
}
export const GetComponentDescriptorByKey_TYPE = function() {
  return 'get'
}
export const GetComponentDescriptorByKeyURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/component/key'
  if (parameters['key'] !== undefined) {
    queryParameters['key'] = parameters['key']
  }
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: DiscoverDependentComponent
 * url: DiscoverDependentComponentURL
 * method: DiscoverDependentComponent_TYPE
 * raw_url: DiscoverDependentComponent_RAW_URL
 * @param id - 
 */
export const DiscoverDependentComponent = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/component/dependency/discovery'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['id'] !== undefined) {
    queryParameters['id'] = parameters['id']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const DiscoverDependentComponent_RAW_URL = function() {
  return '/component/dependency/discovery'
}
export const DiscoverDependentComponent_TYPE = function() {
  return 'get'
}
export const DiscoverDependentComponentURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/component/dependency/discovery'
  if (parameters['id'] !== undefined) {
    queryParameters['id'] = parameters['id']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetDependentComponentLatestVersion
 * url: GetDependentComponentLatestVersionURL
 * method: GetDependentComponentLatestVersion_TYPE
 * raw_url: GetDependentComponentLatestVersion_RAW_URL
 * @param id - 
 * @param fromCache - 
 */
export const GetDependentComponentLatestVersion = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/component/dependency/latest-version'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['id'] !== undefined) {
    queryParameters['id'] = parameters['id']
  }
  if (parameters['fromCache'] !== undefined) {
    queryParameters['fromCache'] = parameters['fromCache']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetDependentComponentLatestVersion_RAW_URL = function() {
  return '/component/dependency/latest-version'
}
export const GetDependentComponentLatestVersion_TYPE = function() {
  return 'get'
}
export const GetDependentComponentLatestVersionURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/component/dependency/latest-version'
  if (parameters['id'] !== undefined) {
    queryParameters['id'] = parameters['id']
  }
  if (parameters['fromCache'] !== undefined) {
    queryParameters['fromCache'] = parameters['fromCache']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: InstallDependentComponent
 * url: InstallDependentComponentURL
 * method: InstallDependentComponent_TYPE
 * raw_url: InstallDependentComponent_RAW_URL
 * @param id - 
 */
export const InstallDependentComponent = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/component/dependency'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['id'] !== undefined) {
    queryParameters['id'] = parameters['id']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const InstallDependentComponent_RAW_URL = function() {
  return '/component/dependency'
}
export const InstallDependentComponent_TYPE = function() {
  return 'post'
}
export const InstallDependentComponentURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/component/dependency'
  if (parameters['id'] !== undefined) {
    queryParameters['id'] = parameters['id']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: AddComponentOptions
 * url: AddComponentOptionsURL
 * method: AddComponentOptions_TYPE
 * raw_url: AddComponentOptions_RAW_URL
 * @param model - 
 */
export const AddComponentOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/component-options'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const AddComponentOptions_RAW_URL = function() {
  return '/component-options'
}
export const AddComponentOptions_TYPE = function() {
  return 'post'
}
export const AddComponentOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/component-options'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PutComponentOptions
 * url: PutComponentOptionsURL
 * method: PutComponentOptions_TYPE
 * raw_url: PutComponentOptions_RAW_URL
 * @param id - 
 * @param model - 
 */
export const PutComponentOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/component-options/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const PutComponentOptions_RAW_URL = function() {
  return '/component-options/{id}'
}
export const PutComponentOptions_TYPE = function() {
  return 'put'
}
export const PutComponentOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/component-options/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveComponentOptions
 * url: RemoveComponentOptionsURL
 * method: RemoveComponentOptions_TYPE
 * raw_url: RemoveComponentOptions_RAW_URL
 * @param id - 
 */
export const RemoveComponentOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/component-options/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveComponentOptions_RAW_URL = function() {
  return '/component-options/{id}'
}
export const RemoveComponentOptions_TYPE = function() {
  return 'delete'
}
export const RemoveComponentOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/component-options/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllExtensionMediaTypes
 * url: GetAllExtensionMediaTypesURL
 * method: GetAllExtensionMediaTypes_TYPE
 * raw_url: GetAllExtensionMediaTypes_RAW_URL
 */
export const GetAllExtensionMediaTypes = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/api/constant/extension-media-types'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllExtensionMediaTypes_RAW_URL = function() {
  return '/api/constant/extension-media-types'
}
export const GetAllExtensionMediaTypes_TYPE = function() {
  return 'get'
}
export const GetAllExtensionMediaTypesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/api/constant/extension-media-types'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: getApiConstant
 * url: getApiConstantURL
 * method: getApiConstant_TYPE
 * raw_url: getApiConstant_RAW_URL
 */
export const getApiConstant = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/api/constant'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const getApiConstant_RAW_URL = function() {
  return '/api/constant'
}
export const getApiConstant_TYPE = function() {
  return 'get'
}
export const getApiConstantURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/api/constant'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllCustomProperties
 * url: GetAllCustomPropertiesURL
 * method: GetAllCustomProperties_TYPE
 * raw_url: GetAllCustomProperties_RAW_URL
 * @param additionalItems - 
 */
export const GetAllCustomProperties = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/custom-property/all'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllCustomProperties_RAW_URL = function() {
  return '/custom-property/all'
}
export const GetAllCustomProperties_TYPE = function() {
  return 'get'
}
export const GetAllCustomPropertiesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/custom-property/all'
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetCustomPropertyByKeys
 * url: GetCustomPropertyByKeysURL
 * method: GetCustomPropertyByKeys_TYPE
 * raw_url: GetCustomPropertyByKeys_RAW_URL
 * @param ids - 
 * @param additionalItems - 
 */
export const GetCustomPropertyByKeys = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/custom-property/ids'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['ids'] !== undefined) {
    queryParameters['ids'] = parameters['ids']
  }
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetCustomPropertyByKeys_RAW_URL = function() {
  return '/custom-property/ids'
}
export const GetCustomPropertyByKeys_TYPE = function() {
  return 'get'
}
export const GetCustomPropertyByKeysURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/custom-property/ids'
  if (parameters['ids'] !== undefined) {
    queryParameters['ids'] = parameters['ids']
  }
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: AddCustomProperty
 * url: AddCustomPropertyURL
 * method: AddCustomProperty_TYPE
 * raw_url: AddCustomProperty_RAW_URL
 * @param model - 
 */
export const AddCustomProperty = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/custom-property'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const AddCustomProperty_RAW_URL = function() {
  return '/custom-property'
}
export const AddCustomProperty_TYPE = function() {
  return 'post'
}
export const AddCustomPropertyURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/custom-property'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PutCustomProperty
 * url: PutCustomPropertyURL
 * method: PutCustomProperty_TYPE
 * raw_url: PutCustomProperty_RAW_URL
 * @param id - 
 * @param model - 
 */
export const PutCustomProperty = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/custom-property/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const PutCustomProperty_RAW_URL = function() {
  return '/custom-property/{id}'
}
export const PutCustomProperty_TYPE = function() {
  return 'put'
}
export const PutCustomPropertyURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/custom-property/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveCustomProperty
 * url: RemoveCustomPropertyURL
 * method: RemoveCustomProperty_TYPE
 * raw_url: RemoveCustomProperty_RAW_URL
 * @param id - 
 */
export const RemoveCustomProperty = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/custom-property/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveCustomProperty_RAW_URL = function() {
  return '/custom-property/{id}'
}
export const RemoveCustomProperty_TYPE = function() {
  return 'delete'
}
export const RemoveCustomPropertyURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/custom-property/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: CalculateCustomPropertyTypeConversionLoss
 * url: CalculateCustomPropertyTypeConversionLossURL
 * method: CalculateCustomPropertyTypeConversionLoss_TYPE
 * raw_url: CalculateCustomPropertyTypeConversionLoss_RAW_URL
 * @param id - 
 * @param type - 
 */
export const CalculateCustomPropertyTypeConversionLoss = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/custom-property/{id}/{type}/loss'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  path = path.replace('{type}', `${parameters['type']}`)
  if (parameters['type'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: type'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const CalculateCustomPropertyTypeConversionLoss_RAW_URL = function() {
  return '/custom-property/{id}/{type}/loss'
}
export const CalculateCustomPropertyTypeConversionLoss_TYPE = function() {
  return 'post'
}
export const CalculateCustomPropertyTypeConversionLossURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/custom-property/{id}/{type}/loss'
  path = path.replace('{id}', `${parameters['id']}`)
  path = path.replace('{type}', `${parameters['type']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: EnableAddingNewDataDynamicallyForCustomProperty
 * url: EnableAddingNewDataDynamicallyForCustomPropertyURL
 * method: EnableAddingNewDataDynamicallyForCustomProperty_TYPE
 * raw_url: EnableAddingNewDataDynamicallyForCustomProperty_RAW_URL
 * @param id - 
 */
export const EnableAddingNewDataDynamicallyForCustomProperty = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/custom-property/{id}/options/adding-new-data-dynamically'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const EnableAddingNewDataDynamicallyForCustomProperty_RAW_URL = function() {
  return '/custom-property/{id}/options/adding-new-data-dynamically'
}
export const EnableAddingNewDataDynamicallyForCustomProperty_TYPE = function() {
  return 'put'
}
export const EnableAddingNewDataDynamicallyForCustomPropertyURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/custom-property/{id}/options/adding-new-data-dynamically'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetStatistics
 * url: GetStatisticsURL
 * method: GetStatistics_TYPE
 * raw_url: GetStatistics_RAW_URL
 */
export const GetStatistics = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/dashboard'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetStatistics_RAW_URL = function() {
  return '/dashboard'
}
export const GetStatistics_TYPE = function() {
  return 'get'
}
export const GetStatisticsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/dashboard'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllDownloaderNamingDefinitions
 * url: GetAllDownloaderNamingDefinitionsURL
 * method: GetAllDownloaderNamingDefinitions_TYPE
 * raw_url: GetAllDownloaderNamingDefinitions_RAW_URL
 */
export const GetAllDownloaderNamingDefinitions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/download-task/downloader/naming-definitions'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllDownloaderNamingDefinitions_RAW_URL = function() {
  return '/download-task/downloader/naming-definitions'
}
export const GetAllDownloaderNamingDefinitions_TYPE = function() {
  return 'get'
}
export const GetAllDownloaderNamingDefinitionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/download-task/downloader/naming-definitions'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllDownloadTasks
 * url: GetAllDownloadTasksURL
 * method: GetAllDownloadTasks_TYPE
 * raw_url: GetAllDownloadTasks_RAW_URL
 */
export const GetAllDownloadTasks = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/download-task'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllDownloadTasks_RAW_URL = function() {
  return '/download-task'
}
export const GetAllDownloadTasks_TYPE = function() {
  return 'get'
}
export const GetAllDownloadTasksURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/download-task'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: CreateDownloadTask
 * url: CreateDownloadTaskURL
 * method: CreateDownloadTask_TYPE
 * raw_url: CreateDownloadTask_RAW_URL
 * @param model - 
 */
export const CreateDownloadTask = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/download-task'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const CreateDownloadTask_RAW_URL = function() {
  return '/download-task'
}
export const CreateDownloadTask_TYPE = function() {
  return 'post'
}
export const CreateDownloadTaskURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/download-task'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetDownloadTask
 * url: GetDownloadTaskURL
 * method: GetDownloadTask_TYPE
 * raw_url: GetDownloadTask_RAW_URL
 * @param id - 
 */
export const GetDownloadTask = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/download-task/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetDownloadTask_RAW_URL = function() {
  return '/download-task/{id}'
}
export const GetDownloadTask_TYPE = function() {
  return 'get'
}
export const GetDownloadTaskURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/download-task/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveDownloadTask
 * url: RemoveDownloadTaskURL
 * method: RemoveDownloadTask_TYPE
 * raw_url: RemoveDownloadTask_RAW_URL
 * @param id - 
 */
export const RemoveDownloadTask = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/download-task/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveDownloadTask_RAW_URL = function() {
  return '/download-task/{id}'
}
export const RemoveDownloadTask_TYPE = function() {
  return 'delete'
}
export const RemoveDownloadTaskURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/download-task/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PutDownloadTask
 * url: PutDownloadTaskURL
 * method: PutDownloadTask_TYPE
 * raw_url: PutDownloadTask_RAW_URL
 * @param id - 
 * @param model - 
 */
export const PutDownloadTask = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/download-task/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const PutDownloadTask_RAW_URL = function() {
  return '/download-task/{id}'
}
export const PutDownloadTask_TYPE = function() {
  return 'put'
}
export const PutDownloadTaskURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/download-task/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveDownloadTasksByIds
 * url: RemoveDownloadTasksByIdsURL
 * method: RemoveDownloadTasksByIds_TYPE
 * raw_url: RemoveDownloadTasksByIds_RAW_URL
 * @param model - 
 */
export const RemoveDownloadTasksByIds = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/download-task/ids'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveDownloadTasksByIds_RAW_URL = function() {
  return '/download-task/ids'
}
export const RemoveDownloadTasksByIds_TYPE = function() {
  return 'delete'
}
export const RemoveDownloadTasksByIdsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/download-task/ids'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StartDownloadTasks
 * url: StartDownloadTasksURL
 * method: StartDownloadTasks_TYPE
 * raw_url: StartDownloadTasks_RAW_URL
 * @param model - 
 */
export const StartDownloadTasks = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/download-task/download'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const StartDownloadTasks_RAW_URL = function() {
  return '/download-task/download'
}
export const StartDownloadTasks_TYPE = function() {
  return 'post'
}
export const StartDownloadTasksURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/download-task/download'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StopDownloadTasks
 * url: StopDownloadTasksURL
 * method: StopDownloadTasks_TYPE
 * raw_url: StopDownloadTasks_RAW_URL
 * @param model - 
 */
export const StopDownloadTasks = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/download-task/download'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const StopDownloadTasks_RAW_URL = function() {
  return '/download-task/download'
}
export const StopDownloadTasks_TYPE = function() {
  return 'delete'
}
export const StopDownloadTasksURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/download-task/download'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetResourceEnhancements
 * url: GetResourceEnhancementsURL
 * method: GetResourceEnhancements_TYPE
 * raw_url: GetResourceEnhancements_RAW_URL
 * @param resourceId - 
 * @param additionalItem - 
 */
export const GetResourceEnhancements = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/{resourceId}/enhancement'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{resourceId}', `${parameters['resourceId']}`)
  if (parameters['resourceId'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: resourceId'))
  }
  if (parameters['additionalItem'] !== undefined) {
    queryParameters['additionalItem'] = parameters['additionalItem']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetResourceEnhancements_RAW_URL = function() {
  return '/resource/{resourceId}/enhancement'
}
export const GetResourceEnhancements_TYPE = function() {
  return 'get'
}
export const GetResourceEnhancementsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/{resourceId}/enhancement'
  path = path.replace('{resourceId}', `${parameters['resourceId']}`)
  if (parameters['additionalItem'] !== undefined) {
    queryParameters['additionalItem'] = parameters['additionalItem']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: DeleteResourceEnhancement
 * url: DeleteResourceEnhancementURL
 * method: DeleteResourceEnhancement_TYPE
 * raw_url: DeleteResourceEnhancement_RAW_URL
 * @param resourceId - 
 * @param enhancerId - 
 */
export const DeleteResourceEnhancement = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/{resourceId}/enhancer/{enhancerId}/enhancement'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{resourceId}', `${parameters['resourceId']}`)
  if (parameters['resourceId'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: resourceId'))
  }
  path = path.replace('{enhancerId}', `${parameters['enhancerId']}`)
  if (parameters['enhancerId'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: enhancerId'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const DeleteResourceEnhancement_RAW_URL = function() {
  return '/resource/{resourceId}/enhancer/{enhancerId}/enhancement'
}
export const DeleteResourceEnhancement_TYPE = function() {
  return 'delete'
}
export const DeleteResourceEnhancementURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/{resourceId}/enhancer/{enhancerId}/enhancement'
  path = path.replace('{resourceId}', `${parameters['resourceId']}`)
  path = path.replace('{enhancerId}', `${parameters['enhancerId']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: CreateEnhancementForResourceByEnhancer
 * url: CreateEnhancementForResourceByEnhancerURL
 * method: CreateEnhancementForResourceByEnhancer_TYPE
 * raw_url: CreateEnhancementForResourceByEnhancer_RAW_URL
 * @param resourceId - 
 * @param enhancerId - 
 */
export const CreateEnhancementForResourceByEnhancer = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/{resourceId}/enhancer/{enhancerId}/enhancement'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{resourceId}', `${parameters['resourceId']}`)
  if (parameters['resourceId'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: resourceId'))
  }
  path = path.replace('{enhancerId}', `${parameters['enhancerId']}`)
  if (parameters['enhancerId'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: enhancerId'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const CreateEnhancementForResourceByEnhancer_RAW_URL = function() {
  return '/resource/{resourceId}/enhancer/{enhancerId}/enhancement'
}
export const CreateEnhancementForResourceByEnhancer_TYPE = function() {
  return 'post'
}
export const CreateEnhancementForResourceByEnhancerURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/{resourceId}/enhancer/{enhancerId}/enhancement'
  path = path.replace('{resourceId}', `${parameters['resourceId']}`)
  path = path.replace('{enhancerId}', `${parameters['enhancerId']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: DeleteByEnhancementsMediaLibrary
 * url: DeleteByEnhancementsMediaLibraryURL
 * method: DeleteByEnhancementsMediaLibrary_TYPE
 * raw_url: DeleteByEnhancementsMediaLibrary_RAW_URL
 * @param mediaLibraryId - 
 */
export const DeleteByEnhancementsMediaLibrary = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/{mediaLibraryId}/enhancement'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{mediaLibraryId}', `${parameters['mediaLibraryId']}`)
  if (parameters['mediaLibraryId'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: mediaLibraryId'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const DeleteByEnhancementsMediaLibrary_RAW_URL = function() {
  return '/media-library/{mediaLibraryId}/enhancement'
}
export const DeleteByEnhancementsMediaLibrary_TYPE = function() {
  return 'delete'
}
export const DeleteByEnhancementsMediaLibraryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/{mediaLibraryId}/enhancement'
  path = path.replace('{mediaLibraryId}', `${parameters['mediaLibraryId']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: DeleteEnhancementsByCategory
 * url: DeleteEnhancementsByCategoryURL
 * method: DeleteEnhancementsByCategory_TYPE
 * raw_url: DeleteEnhancementsByCategory_RAW_URL
 * @param categoryId - 
 */
export const DeleteEnhancementsByCategory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/category/{categoryId}/enhancement'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{categoryId}', `${parameters['categoryId']}`)
  if (parameters['categoryId'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: categoryId'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const DeleteEnhancementsByCategory_RAW_URL = function() {
  return '/category/{categoryId}/enhancement'
}
export const DeleteEnhancementsByCategory_TYPE = function() {
  return 'delete'
}
export const DeleteEnhancementsByCategoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/category/{categoryId}/enhancement'
  path = path.replace('{categoryId}', `${parameters['categoryId']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllEnhancerDescriptors
 * url: GetAllEnhancerDescriptorsURL
 * method: GetAllEnhancerDescriptors_TYPE
 * raw_url: GetAllEnhancerDescriptors_RAW_URL
 */
export const GetAllEnhancerDescriptors = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/enhancer/descriptor'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllEnhancerDescriptors_RAW_URL = function() {
  return '/enhancer/descriptor'
}
export const GetAllEnhancerDescriptors_TYPE = function() {
  return 'get'
}
export const GetAllEnhancerDescriptorsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/enhancer/descriptor'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetEntryTaskInfo
 * url: GetEntryTaskInfoURL
 * method: GetEntryTaskInfo_TYPE
 * raw_url: GetEntryTaskInfo_RAW_URL
 * @param path - 
 */
export const GetEntryTaskInfo = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/task-info'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetEntryTaskInfo_RAW_URL = function() {
  return '/file/task-info'
}
export const GetEntryTaskInfo_TYPE = function() {
  return 'get'
}
export const GetEntryTaskInfoURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/task-info'
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetIwFsInfo
 * url: GetIwFsInfoURL
 * method: GetIwFsInfo_TYPE
 * raw_url: GetIwFsInfo_RAW_URL
 * @param path - 
 */
export const GetIwFsInfo = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/iwfs-info'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetIwFsInfo_RAW_URL = function() {
  return '/file/iwfs-info'
}
export const GetIwFsInfo_TYPE = function() {
  return 'get'
}
export const GetIwFsInfoURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/iwfs-info'
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetIwFsEntry
 * url: GetIwFsEntryURL
 * method: GetIwFsEntry_TYPE
 * raw_url: GetIwFsEntry_RAW_URL
 * @param path - 
 */
export const GetIwFsEntry = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/iwfs-entry'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetIwFsEntry_RAW_URL = function() {
  return '/file/iwfs-entry'
}
export const GetIwFsEntry_TYPE = function() {
  return 'get'
}
export const GetIwFsEntryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/iwfs-entry'
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: CreateDirectory
 * url: CreateDirectoryURL
 * method: CreateDirectory_TYPE
 * raw_url: CreateDirectory_RAW_URL
 * @param parent - 
 */
export const CreateDirectory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/directory'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['parent'] !== undefined) {
    queryParameters['parent'] = parameters['parent']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const CreateDirectory_RAW_URL = function() {
  return '/file/directory'
}
export const CreateDirectory_TYPE = function() {
  return 'post'
}
export const CreateDirectoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/directory'
  if (parameters['parent'] !== undefined) {
    queryParameters['parent'] = parameters['parent']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetChildrenIwFsInfo
 * url: GetChildrenIwFsInfoURL
 * method: GetChildrenIwFsInfo_TYPE
 * raw_url: GetChildrenIwFsInfo_RAW_URL
 * @param root - 
 */
export const GetChildrenIwFsInfo = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/children/iwfs-info'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['root'] !== undefined) {
    queryParameters['root'] = parameters['root']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetChildrenIwFsInfo_RAW_URL = function() {
  return '/file/children/iwfs-info'
}
export const GetChildrenIwFsInfo_TYPE = function() {
  return 'get'
}
export const GetChildrenIwFsInfoURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/children/iwfs-info'
  if (parameters['root'] !== undefined) {
    queryParameters['root'] = parameters['root']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveFiles
 * url: RemoveFilesURL
 * method: RemoveFiles_TYPE
 * raw_url: RemoveFiles_RAW_URL
 * @param model - 
 */
export const RemoveFiles = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveFiles_RAW_URL = function() {
  return '/file'
}
export const RemoveFiles_TYPE = function() {
  return 'delete'
}
export const RemoveFilesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RenameFile
 * url: RenameFileURL
 * method: RenameFile_TYPE
 * raw_url: RenameFile_RAW_URL
 * @param model - 
 */
export const RenameFile = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/name'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const RenameFile_RAW_URL = function() {
  return '/file/name'
}
export const RenameFile_TYPE = function() {
  return 'put'
}
export const RenameFileURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/name'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: OpenRecycleBin
 * url: OpenRecycleBinURL
 * method: OpenRecycleBin_TYPE
 * raw_url: OpenRecycleBin_RAW_URL
 */
export const OpenRecycleBin = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/recycle-bin'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const OpenRecycleBin_RAW_URL = function() {
  return '/file/recycle-bin'
}
export const OpenRecycleBin_TYPE = function() {
  return 'get'
}
export const OpenRecycleBinURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/recycle-bin'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: ExtractAndRemoveDirectory
 * url: ExtractAndRemoveDirectoryURL
 * method: ExtractAndRemoveDirectory_TYPE
 * raw_url: ExtractAndRemoveDirectory_RAW_URL
 * @param directory - 
 */
export const ExtractAndRemoveDirectory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/extract-and-remove-directory'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['directory'] !== undefined) {
    queryParameters['directory'] = parameters['directory']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const ExtractAndRemoveDirectory_RAW_URL = function() {
  return '/file/extract-and-remove-directory'
}
export const ExtractAndRemoveDirectory_TYPE = function() {
  return 'post'
}
export const ExtractAndRemoveDirectoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/extract-and-remove-directory'
  if (parameters['directory'] !== undefined) {
    queryParameters['directory'] = parameters['directory']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: MoveEntries
 * url: MoveEntriesURL
 * method: MoveEntries_TYPE
 * raw_url: MoveEntries_RAW_URL
 * @param model - 
 */
export const MoveEntries = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/move-entries'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const MoveEntries_RAW_URL = function() {
  return '/file/move-entries'
}
export const MoveEntries_TYPE = function() {
  return 'post'
}
export const MoveEntriesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/move-entries'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetSameNameEntriesInWorkingDirectory
 * url: GetSameNameEntriesInWorkingDirectoryURL
 * method: GetSameNameEntriesInWorkingDirectory_TYPE
 * raw_url: GetSameNameEntriesInWorkingDirectory_RAW_URL
 * @param model - 
 */
export const GetSameNameEntriesInWorkingDirectory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/same-name-entries-in-working-directory'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const GetSameNameEntriesInWorkingDirectory_RAW_URL = function() {
  return '/file/same-name-entries-in-working-directory'
}
export const GetSameNameEntriesInWorkingDirectory_TYPE = function() {
  return 'post'
}
export const GetSameNameEntriesInWorkingDirectoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/same-name-entries-in-working-directory'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveSameNameEntryInWorkingDirectory
 * url: RemoveSameNameEntryInWorkingDirectoryURL
 * method: RemoveSameNameEntryInWorkingDirectory_TYPE
 * raw_url: RemoveSameNameEntryInWorkingDirectory_RAW_URL
 * @param model - 
 */
export const RemoveSameNameEntryInWorkingDirectory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/same-name-entry-in-working-directory'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveSameNameEntryInWorkingDirectory_RAW_URL = function() {
  return '/file/same-name-entry-in-working-directory'
}
export const RemoveSameNameEntryInWorkingDirectory_TYPE = function() {
  return 'delete'
}
export const RemoveSameNameEntryInWorkingDirectoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/same-name-entry-in-working-directory'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StandardizeEntryName
 * url: StandardizeEntryNameURL
 * method: StandardizeEntryName_TYPE
 * raw_url: StandardizeEntryName_RAW_URL
 * @param path - 
 */
export const StandardizeEntryName = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/standardize'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const StandardizeEntryName_RAW_URL = function() {
  return '/file/standardize'
}
export const StandardizeEntryName_TYPE = function() {
  return 'put'
}
export const StandardizeEntryNameURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/standardize'
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PlayFile
 * url: PlayFileURL
 * method: PlayFile_TYPE
 * raw_url: PlayFile_RAW_URL
 * @param fullname - 
 */
export const PlayFile = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/play'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['fullname'] !== undefined) {
    queryParameters['fullname'] = parameters['fullname']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const PlayFile_RAW_URL = function() {
  return '/file/play'
}
export const PlayFile_TYPE = function() {
  return 'get'
}
export const PlayFileURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/play'
  if (parameters['fullname'] !== undefined) {
    queryParameters['fullname'] = parameters['fullname']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: DecompressFiles
 * url: DecompressFilesURL
 * method: DecompressFiles_TYPE
 * raw_url: DecompressFiles_RAW_URL
 * @param model - 
 */
export const DecompressFiles = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/decompression'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const DecompressFiles_RAW_URL = function() {
  return '/file/decompression'
}
export const DecompressFiles_TYPE = function() {
  return 'post'
}
export const DecompressFilesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/decompression'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetIconData
 * url: GetIconDataURL
 * method: GetIconData_TYPE
 * raw_url: GetIconData_RAW_URL
 * @param path - 
 */
export const GetIconData = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/icon'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetIconData_RAW_URL = function() {
  return '/file/icon'
}
export const GetIconData_TYPE = function() {
  return 'get'
}
export const GetIconDataURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/icon'
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllFiles
 * url: GetAllFilesURL
 * method: GetAllFiles_TYPE
 * raw_url: GetAllFiles_RAW_URL
 * @param path - 
 */
export const GetAllFiles = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/all-files'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllFiles_RAW_URL = function() {
  return '/file/all-files'
}
export const GetAllFiles_TYPE = function() {
  return 'get'
}
export const GetAllFilesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/all-files'
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetCompressedFileEntries
 * url: GetCompressedFileEntriesURL
 * method: GetCompressedFileEntries_TYPE
 * raw_url: GetCompressedFileEntries_RAW_URL
 * @param compressedFilePath - 
 */
export const GetCompressedFileEntries = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/compressed-file/entries'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['compressedFilePath'] !== undefined) {
    queryParameters['compressedFilePath'] = parameters['compressedFilePath']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetCompressedFileEntries_RAW_URL = function() {
  return '/file/compressed-file/entries'
}
export const GetCompressedFileEntries_TYPE = function() {
  return 'get'
}
export const GetCompressedFileEntriesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/compressed-file/entries'
  if (parameters['compressedFilePath'] !== undefined) {
    queryParameters['compressedFilePath'] = parameters['compressedFilePath']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetFileExtensionCounts
 * url: GetFileExtensionCountsURL
 * method: GetFileExtensionCounts_TYPE
 * raw_url: GetFileExtensionCounts_RAW_URL
 * @param sampleFile - 
 * @param rootPath - 
 */
export const GetFileExtensionCounts = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/file-extension-counts'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['sampleFile'] !== undefined) {
    queryParameters['sampleFile'] = parameters['sampleFile']
  }
  if (parameters['rootPath'] !== undefined) {
    queryParameters['rootPath'] = parameters['rootPath']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetFileExtensionCounts_RAW_URL = function() {
  return '/file/file-extension-counts'
}
export const GetFileExtensionCounts_TYPE = function() {
  return 'get'
}
export const GetFileExtensionCountsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/file-extension-counts'
  if (parameters['sampleFile'] !== undefined) {
    queryParameters['sampleFile'] = parameters['sampleFile']
  }
  if (parameters['rootPath'] !== undefined) {
    queryParameters['rootPath'] = parameters['rootPath']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PreviewFileEntriesMergeResult
 * url: PreviewFileEntriesMergeResultURL
 * method: PreviewFileEntriesMergeResult_TYPE
 * raw_url: PreviewFileEntriesMergeResult_RAW_URL
 * @param model - 
 */
export const PreviewFileEntriesMergeResult = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/merge-preview'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const PreviewFileEntriesMergeResult_RAW_URL = function() {
  return '/file/merge-preview'
}
export const PreviewFileEntriesMergeResult_TYPE = function() {
  return 'put'
}
export const PreviewFileEntriesMergeResultURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/merge-preview'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PreviewFileEntriesMergeResultInRootPath
 * url: PreviewFileEntriesMergeResultInRootPathURL
 * method: PreviewFileEntriesMergeResultInRootPath_TYPE
 * raw_url: PreviewFileEntriesMergeResultInRootPath_RAW_URL
 * @param model - 
 */
export const PreviewFileEntriesMergeResultInRootPath = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/merge-preview-in-root-path'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const PreviewFileEntriesMergeResultInRootPath_RAW_URL = function() {
  return '/file/merge-preview-in-root-path'
}
export const PreviewFileEntriesMergeResultInRootPath_TYPE = function() {
  return 'put'
}
export const PreviewFileEntriesMergeResultInRootPathURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/merge-preview-in-root-path'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: MergeFileEntries
 * url: MergeFileEntriesURL
 * method: MergeFileEntries_TYPE
 * raw_url: MergeFileEntries_RAW_URL
 * @param model - 
 */
export const MergeFileEntries = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/merge'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const MergeFileEntries_RAW_URL = function() {
  return '/file/merge'
}
export const MergeFileEntries_TYPE = function() {
  return 'put'
}
export const MergeFileEntriesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/merge'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: MergeFileEntriesInRootPath
 * url: MergeFileEntriesInRootPathURL
 * method: MergeFileEntriesInRootPath_TYPE
 * raw_url: MergeFileEntriesInRootPath_RAW_URL
 * @param model - 
 */
export const MergeFileEntriesInRootPath = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/merge-by'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const MergeFileEntriesInRootPath_RAW_URL = function() {
  return '/file/merge-by'
}
export const MergeFileEntriesInRootPath_TYPE = function() {
  return 'put'
}
export const MergeFileEntriesInRootPathURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/merge-by'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetFileSystemEntriesInDirectory
 * url: GetFileSystemEntriesInDirectoryURL
 * method: GetFileSystemEntriesInDirectory_TYPE
 * raw_url: GetFileSystemEntriesInDirectory_RAW_URL
 * @param path - 
 * @param maxCount - 
 */
export const GetFileSystemEntriesInDirectory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/directory/file-entries'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters['maxCount'] !== undefined) {
    queryParameters['maxCount'] = parameters['maxCount']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetFileSystemEntriesInDirectory_RAW_URL = function() {
  return '/file/directory/file-entries'
}
export const GetFileSystemEntriesInDirectory_TYPE = function() {
  return 'get'
}
export const GetFileSystemEntriesInDirectoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/directory/file-entries'
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters['maxCount'] !== undefined) {
    queryParameters['maxCount'] = parameters['maxCount']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StartWatchingChangesInFileProcessorWorkspace
 * url: StartWatchingChangesInFileProcessorWorkspaceURL
 * method: StartWatchingChangesInFileProcessorWorkspace_TYPE
 * raw_url: StartWatchingChangesInFileProcessorWorkspace_RAW_URL
 * @param path - 
 */
export const StartWatchingChangesInFileProcessorWorkspace = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/file-processor-watcher'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const StartWatchingChangesInFileProcessorWorkspace_RAW_URL = function() {
  return '/file/file-processor-watcher'
}
export const StartWatchingChangesInFileProcessorWorkspace_TYPE = function() {
  return 'post'
}
export const StartWatchingChangesInFileProcessorWorkspaceURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/file-processor-watcher'
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StopWatchingChangesInFileProcessorWorkspace
 * url: StopWatchingChangesInFileProcessorWorkspaceURL
 * method: StopWatchingChangesInFileProcessorWorkspace_TYPE
 * raw_url: StopWatchingChangesInFileProcessorWorkspace_RAW_URL
 */
export const StopWatchingChangesInFileProcessorWorkspace = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/file/file-processor-watcher'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const StopWatchingChangesInFileProcessorWorkspace_RAW_URL = function() {
  return '/file/file-processor-watcher'
}
export const StopWatchingChangesInFileProcessorWorkspace_TYPE = function() {
  return 'delete'
}
export const StopWatchingChangesInFileProcessorWorkspaceURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/file/file-processor-watcher'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: OpenFilesSelector
 * url: OpenFilesSelectorURL
 * method: OpenFilesSelector_TYPE
 * raw_url: OpenFilesSelector_RAW_URL
 * @param initialDirectory - 
 */
export const OpenFilesSelector = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/gui/files-selector'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['initialDirectory'] !== undefined) {
    queryParameters['initialDirectory'] = parameters['initialDirectory']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const OpenFilesSelector_RAW_URL = function() {
  return '/gui/files-selector'
}
export const OpenFilesSelector_TYPE = function() {
  return 'get'
}
export const OpenFilesSelectorURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/gui/files-selector'
  if (parameters['initialDirectory'] !== undefined) {
    queryParameters['initialDirectory'] = parameters['initialDirectory']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: OpenFileSelector
 * url: OpenFileSelectorURL
 * method: OpenFileSelector_TYPE
 * raw_url: OpenFileSelector_RAW_URL
 * @param initialDirectory - 
 */
export const OpenFileSelector = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/gui/file-selector'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['initialDirectory'] !== undefined) {
    queryParameters['initialDirectory'] = parameters['initialDirectory']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const OpenFileSelector_RAW_URL = function() {
  return '/gui/file-selector'
}
export const OpenFileSelector_TYPE = function() {
  return 'get'
}
export const OpenFileSelectorURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/gui/file-selector'
  if (parameters['initialDirectory'] !== undefined) {
    queryParameters['initialDirectory'] = parameters['initialDirectory']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: OpenFolderSelector
 * url: OpenFolderSelectorURL
 * method: OpenFolderSelector_TYPE
 * raw_url: OpenFolderSelector_RAW_URL
 * @param initialDirectory - 
 */
export const OpenFolderSelector = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/gui/folder-selector'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['initialDirectory'] !== undefined) {
    queryParameters['initialDirectory'] = parameters['initialDirectory']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const OpenFolderSelector_RAW_URL = function() {
  return '/gui/folder-selector'
}
export const OpenFolderSelector_TYPE = function() {
  return 'get'
}
export const OpenFolderSelectorURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/gui/folder-selector'
  if (parameters['initialDirectory'] !== undefined) {
    queryParameters['initialDirectory'] = parameters['initialDirectory']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: OpenUrlInDefaultBrowser
 * url: OpenUrlInDefaultBrowserURL
 * method: OpenUrlInDefaultBrowser_TYPE
 * raw_url: OpenUrlInDefaultBrowser_RAW_URL
 * @param url - 
 */
export const OpenUrlInDefaultBrowser = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/gui/url'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['url'] !== undefined) {
    queryParameters['url'] = parameters['url']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const OpenUrlInDefaultBrowser_RAW_URL = function() {
  return '/gui/url'
}
export const OpenUrlInDefaultBrowser_TYPE = function() {
  return 'get'
}
export const OpenUrlInDefaultBrowserURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/gui/url'
  if (parameters['url'] !== undefined) {
    queryParameters['url'] = parameters['url']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllLogs
 * url: GetAllLogsURL
 * method: GetAllLogs_TYPE
 * raw_url: GetAllLogs_RAW_URL
 */
export const GetAllLogs = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/log'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllLogs_RAW_URL = function() {
  return '/log'
}
export const GetAllLogs_TYPE = function() {
  return 'get'
}
export const GetAllLogsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/log'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: ClearAllLog
 * url: ClearAllLogURL
 * method: ClearAllLog_TYPE
 * raw_url: ClearAllLog_RAW_URL
 */
export const ClearAllLog = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/log'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const ClearAllLog_RAW_URL = function() {
  return '/log'
}
export const ClearAllLog_TYPE = function() {
  return 'delete'
}
export const ClearAllLogURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/log'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: SearchLogs
 * url: SearchLogsURL
 * method: SearchLogs_TYPE
 * raw_url: SearchLogs_RAW_URL
 * @param level - 
 * @param startDt - 
 * @param endDt - 
 * @param logger - 
 * @param event - 
 * @param message - 
 * @param pageIndex - 
 * @param pageSize - 
 * @param skipCount - 
 */
export const SearchLogs = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/log/filtered'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['level'] !== undefined) {
    queryParameters['level'] = parameters['level']
  }
  if (parameters['startDt'] !== undefined) {
    queryParameters['startDt'] = parameters['startDt']
  }
  if (parameters['endDt'] !== undefined) {
    queryParameters['endDt'] = parameters['endDt']
  }
  if (parameters['logger'] !== undefined) {
    queryParameters['logger'] = parameters['logger']
  }
  if (parameters['event'] !== undefined) {
    queryParameters['event'] = parameters['event']
  }
  if (parameters['message'] !== undefined) {
    queryParameters['message'] = parameters['message']
  }
  if (parameters['pageIndex'] !== undefined) {
    queryParameters['pageIndex'] = parameters['pageIndex']
  }
  if (parameters['pageSize'] !== undefined) {
    queryParameters['pageSize'] = parameters['pageSize']
  }
  if (parameters['skipCount'] !== undefined) {
    queryParameters['skipCount'] = parameters['skipCount']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const SearchLogs_RAW_URL = function() {
  return '/log/filtered'
}
export const SearchLogs_TYPE = function() {
  return 'get'
}
export const SearchLogsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/log/filtered'
  if (parameters['level'] !== undefined) {
    queryParameters['level'] = parameters['level']
  }
  if (parameters['startDt'] !== undefined) {
    queryParameters['startDt'] = parameters['startDt']
  }
  if (parameters['endDt'] !== undefined) {
    queryParameters['endDt'] = parameters['endDt']
  }
  if (parameters['logger'] !== undefined) {
    queryParameters['logger'] = parameters['logger']
  }
  if (parameters['event'] !== undefined) {
    queryParameters['event'] = parameters['event']
  }
  if (parameters['message'] !== undefined) {
    queryParameters['message'] = parameters['message']
  }
  if (parameters['pageIndex'] !== undefined) {
    queryParameters['pageIndex'] = parameters['pageIndex']
  }
  if (parameters['pageSize'] !== undefined) {
    queryParameters['pageSize'] = parameters['pageSize']
  }
  if (parameters['skipCount'] !== undefined) {
    queryParameters['skipCount'] = parameters['skipCount']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetUnreadLogCount
 * url: GetUnreadLogCountURL
 * method: GetUnreadLogCount_TYPE
 * raw_url: GetUnreadLogCount_RAW_URL
 */
export const GetUnreadLogCount = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/log/unread/count'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetUnreadLogCount_RAW_URL = function() {
  return '/log/unread/count'
}
export const GetUnreadLogCount_TYPE = function() {
  return 'get'
}
export const GetUnreadLogCountURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/log/unread/count'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: ReadLog
 * url: ReadLogURL
 * method: ReadLog_TYPE
 * raw_url: ReadLog_RAW_URL
 * @param id - 
 */
export const ReadLog = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/log/{id}/read'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const ReadLog_RAW_URL = function() {
  return '/log/{id}/read'
}
export const ReadLog_TYPE = function() {
  return 'patch'
}
export const ReadLogURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/log/{id}/read'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: ReadAllLog
 * url: ReadAllLogURL
 * method: ReadAllLog_TYPE
 * raw_url: ReadAllLog_RAW_URL
 */
export const ReadAllLog = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/log/read'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const ReadAllLog_RAW_URL = function() {
  return '/log/read'
}
export const ReadAllLog_TYPE = function() {
  return 'patch'
}
export const ReadAllLogURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/log/read'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllMediaLibraries
 * url: GetAllMediaLibrariesURL
 * method: GetAllMediaLibraries_TYPE
 * raw_url: GetAllMediaLibraries_RAW_URL
 * @param additionalItems - 
 */
export const GetAllMediaLibraries = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllMediaLibraries_RAW_URL = function() {
  return '/media-library'
}
export const GetAllMediaLibraries_TYPE = function() {
  return 'get'
}
export const GetAllMediaLibrariesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library'
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: AddMediaLibrary
 * url: AddMediaLibraryURL
 * method: AddMediaLibrary_TYPE
 * raw_url: AddMediaLibrary_RAW_URL
 * @param model - 
 */
export const AddMediaLibrary = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const AddMediaLibrary_RAW_URL = function() {
  return '/media-library'
}
export const AddMediaLibrary_TYPE = function() {
  return 'post'
}
export const AddMediaLibraryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetMediaLibrary
 * url: GetMediaLibraryURL
 * method: GetMediaLibrary_TYPE
 * raw_url: GetMediaLibrary_RAW_URL
 * @param id - 
 * @param additionalItems - 
 */
export const GetMediaLibrary = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetMediaLibrary_RAW_URL = function() {
  return '/media-library/{id}'
}
export const GetMediaLibrary_TYPE = function() {
  return 'get'
}
export const GetMediaLibraryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: DeleteMediaLibrary
 * url: DeleteMediaLibraryURL
 * method: DeleteMediaLibrary_TYPE
 * raw_url: DeleteMediaLibrary_RAW_URL
 * @param id - 
 */
export const DeleteMediaLibrary = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const DeleteMediaLibrary_RAW_URL = function() {
  return '/media-library/{id}'
}
export const DeleteMediaLibrary_TYPE = function() {
  return 'delete'
}
export const DeleteMediaLibraryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchMediaLibrary
 * url: PatchMediaLibraryURL
 * method: PatchMediaLibrary_TYPE
 * raw_url: PatchMediaLibrary_RAW_URL
 * @param id - 
 * @param model - 
 */
export const PatchMediaLibrary = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const PatchMediaLibrary_RAW_URL = function() {
  return '/media-library/{id}'
}
export const PatchMediaLibrary_TYPE = function() {
  return 'put'
}
export const PatchMediaLibraryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StartSyncMediaLibrary
 * url: StartSyncMediaLibraryURL
 * method: StartSyncMediaLibrary_TYPE
 * raw_url: StartSyncMediaLibrary_RAW_URL
 */
export const StartSyncMediaLibrary = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/sync'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const StartSyncMediaLibrary_RAW_URL = function() {
  return '/media-library/sync'
}
export const StartSyncMediaLibrary_TYPE = function() {
  return 'put'
}
export const StartSyncMediaLibraryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/sync'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StopSyncMediaLibrary
 * url: StopSyncMediaLibraryURL
 * method: StopSyncMediaLibrary_TYPE
 * raw_url: StopSyncMediaLibrary_RAW_URL
 */
export const StopSyncMediaLibrary = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/sync'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const StopSyncMediaLibrary_RAW_URL = function() {
  return '/media-library/sync'
}
export const StopSyncMediaLibrary_TYPE = function() {
  return 'delete'
}
export const StopSyncMediaLibraryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/sync'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: ValidatePathConfiguration
 * url: ValidatePathConfigurationURL
 * method: ValidatePathConfiguration_TYPE
 * raw_url: ValidatePathConfiguration_RAW_URL
 * @param model - 
 */
export const ValidatePathConfiguration = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/path-configuration-validation'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const ValidatePathConfiguration_RAW_URL = function() {
  return '/media-library/path-configuration-validation'
}
export const ValidatePathConfiguration_TYPE = function() {
  return 'post'
}
export const ValidatePathConfigurationURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/path-configuration-validation'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: SortMediaLibrariesInCategory
 * url: SortMediaLibrariesInCategoryURL
 * method: SortMediaLibrariesInCategory_TYPE
 * raw_url: SortMediaLibrariesInCategory_RAW_URL
 * @param model - 
 */
export const SortMediaLibrariesInCategory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/orders-in-category'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const SortMediaLibrariesInCategory_RAW_URL = function() {
  return '/media-library/orders-in-category'
}
export const SortMediaLibrariesInCategory_TYPE = function() {
  return 'put'
}
export const SortMediaLibrariesInCategoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/orders-in-category'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: AddMediaLibraryPathConfiguration
 * url: AddMediaLibraryPathConfigurationURL
 * method: AddMediaLibraryPathConfiguration_TYPE
 * raw_url: AddMediaLibraryPathConfiguration_RAW_URL
 * @param id - 
 * @param model - 
 */
export const AddMediaLibraryPathConfiguration = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/{id}/path-configuration'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const AddMediaLibraryPathConfiguration_RAW_URL = function() {
  return '/media-library/{id}/path-configuration'
}
export const AddMediaLibraryPathConfiguration_TYPE = function() {
  return 'post'
}
export const AddMediaLibraryPathConfigurationURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/{id}/path-configuration'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RemoveMediaLibraryPathConfiguration
 * url: RemoveMediaLibraryPathConfigurationURL
 * method: RemoveMediaLibraryPathConfiguration_TYPE
 * raw_url: RemoveMediaLibraryPathConfiguration_RAW_URL
 * @param id - 
 * @param model - 
 */
export const RemoveMediaLibraryPathConfiguration = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/{id}/path-configuration'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const RemoveMediaLibraryPathConfiguration_RAW_URL = function() {
  return '/media-library/{id}/path-configuration'
}
export const RemoveMediaLibraryPathConfiguration_TYPE = function() {
  return 'delete'
}
export const RemoveMediaLibraryPathConfigurationURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/{id}/path-configuration'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: AddMediaLibrariesInBulk
 * url: AddMediaLibrariesInBulkURL
 * method: AddMediaLibrariesInBulk_TYPE
 * raw_url: AddMediaLibrariesInBulk_RAW_URL
 * @param cId - 
 * @param model - 
 */
export const AddMediaLibrariesInBulk = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/bulk-add/{cId}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{cId}', `${parameters['cId']}`)
  if (parameters['cId'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: cId'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const AddMediaLibrariesInBulk_RAW_URL = function() {
  return '/media-library/bulk-add/{cId}'
}
export const AddMediaLibrariesInBulk_TYPE = function() {
  return 'post'
}
export const AddMediaLibrariesInBulkURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/bulk-add/{cId}'
  path = path.replace('{cId}', `${parameters['cId']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: AddMediaLibraryRootPathsInBulk
 * url: AddMediaLibraryRootPathsInBulkURL
 * method: AddMediaLibraryRootPathsInBulk_TYPE
 * raw_url: AddMediaLibraryRootPathsInBulk_RAW_URL
 * @param mlId - 
 * @param model - 
 */
export const AddMediaLibraryRootPathsInBulk = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/{mlId}/path-configuration/root-paths'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{mlId}', `${parameters['mlId']}`)
  if (parameters['mlId'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: mlId'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const AddMediaLibraryRootPathsInBulk_RAW_URL = function() {
  return '/media-library/{mlId}/path-configuration/root-paths'
}
export const AddMediaLibraryRootPathsInBulk_TYPE = function() {
  return 'post'
}
export const AddMediaLibraryRootPathsInBulkURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/{mlId}/path-configuration/root-paths'
  path = path.replace('{mlId}', `${parameters['mlId']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StartSyncingMediaLibraryResources
 * url: StartSyncingMediaLibraryResourcesURL
 * method: StartSyncingMediaLibraryResources_TYPE
 * raw_url: StartSyncingMediaLibraryResources_RAW_URL
 * @param id - 
 */
export const StartSyncingMediaLibraryResources = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/media-library/{id}/synchronization'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const StartSyncingMediaLibraryResources_RAW_URL = function() {
  return '/media-library/{id}/synchronization'
}
export const StartSyncingMediaLibraryResources_TYPE = function() {
  return 'put'
}
export const StartSyncingMediaLibraryResourcesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/media-library/{id}/synchronization'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAppOptions
 * url: GetAppOptionsURL
 * method: GetAppOptions_TYPE
 * raw_url: GetAppOptions_RAW_URL
 */
export const GetAppOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/app'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAppOptions_RAW_URL = function() {
  return '/options/app'
}
export const GetAppOptions_TYPE = function() {
  return 'get'
}
export const GetAppOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/app'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchAppOptions
 * url: PatchAppOptionsURL
 * method: PatchAppOptions_TYPE
 * raw_url: PatchAppOptions_RAW_URL
 * @param model - 
 */
export const PatchAppOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/app'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const PatchAppOptions_RAW_URL = function() {
  return '/options/app'
}
export const PatchAppOptions_TYPE = function() {
  return 'patch'
}
export const PatchAppOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/app'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetUIOptions
 * url: GetUIOptionsURL
 * method: GetUIOptions_TYPE
 * raw_url: GetUIOptions_RAW_URL
 */
export const GetUIOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/ui'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetUIOptions_RAW_URL = function() {
  return '/options/ui'
}
export const GetUIOptions_TYPE = function() {
  return 'get'
}
export const GetUIOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/ui'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchUIOptions
 * url: PatchUIOptionsURL
 * method: PatchUIOptions_TYPE
 * raw_url: PatchUIOptions_RAW_URL
 * @param model - 
 */
export const PatchUIOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/ui'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const PatchUIOptions_RAW_URL = function() {
  return '/options/ui'
}
export const PatchUIOptions_TYPE = function() {
  return 'patch'
}
export const PatchUIOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/ui'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetBilibiliOptions
 * url: GetBilibiliOptionsURL
 * method: GetBilibiliOptions_TYPE
 * raw_url: GetBilibiliOptions_RAW_URL
 */
export const GetBilibiliOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/bilibili'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetBilibiliOptions_RAW_URL = function() {
  return '/options/bilibili'
}
export const GetBilibiliOptions_TYPE = function() {
  return 'get'
}
export const GetBilibiliOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/bilibili'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchBilibiliOptions
 * url: PatchBilibiliOptionsURL
 * method: PatchBilibiliOptions_TYPE
 * raw_url: PatchBilibiliOptions_RAW_URL
 * @param model - 
 */
export const PatchBilibiliOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/bilibili'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const PatchBilibiliOptions_RAW_URL = function() {
  return '/options/bilibili'
}
export const PatchBilibiliOptions_TYPE = function() {
  return 'patch'
}
export const PatchBilibiliOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/bilibili'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetExHentaiOptions
 * url: GetExHentaiOptionsURL
 * method: GetExHentaiOptions_TYPE
 * raw_url: GetExHentaiOptions_RAW_URL
 */
export const GetExHentaiOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/exhentai'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetExHentaiOptions_RAW_URL = function() {
  return '/options/exhentai'
}
export const GetExHentaiOptions_TYPE = function() {
  return 'get'
}
export const GetExHentaiOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/exhentai'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchExHentaiOptions
 * url: PatchExHentaiOptionsURL
 * method: PatchExHentaiOptions_TYPE
 * raw_url: PatchExHentaiOptions_RAW_URL
 * @param model - 
 */
export const PatchExHentaiOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/exhentai'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const PatchExHentaiOptions_RAW_URL = function() {
  return '/options/exhentai'
}
export const PatchExHentaiOptions_TYPE = function() {
  return 'patch'
}
export const PatchExHentaiOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/exhentai'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetFileSystemOptions
 * url: GetFileSystemOptionsURL
 * method: GetFileSystemOptions_TYPE
 * raw_url: GetFileSystemOptions_RAW_URL
 */
export const GetFileSystemOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/filesystem'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetFileSystemOptions_RAW_URL = function() {
  return '/options/filesystem'
}
export const GetFileSystemOptions_TYPE = function() {
  return 'get'
}
export const GetFileSystemOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/filesystem'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchFileSystemOptions
 * url: PatchFileSystemOptionsURL
 * method: PatchFileSystemOptions_TYPE
 * raw_url: PatchFileSystemOptions_RAW_URL
 * @param model - 
 */
export const PatchFileSystemOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/filesystem'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const PatchFileSystemOptions_RAW_URL = function() {
  return '/options/filesystem'
}
export const PatchFileSystemOptions_TYPE = function() {
  return 'patch'
}
export const PatchFileSystemOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/filesystem'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetJavLibraryOptions
 * url: GetJavLibraryOptionsURL
 * method: GetJavLibraryOptions_TYPE
 * raw_url: GetJavLibraryOptions_RAW_URL
 */
export const GetJavLibraryOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/javlibrary'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetJavLibraryOptions_RAW_URL = function() {
  return '/options/javlibrary'
}
export const GetJavLibraryOptions_TYPE = function() {
  return 'get'
}
export const GetJavLibraryOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/javlibrary'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchJavLibraryOptions
 * url: PatchJavLibraryOptionsURL
 * method: PatchJavLibraryOptions_TYPE
 * raw_url: PatchJavLibraryOptions_RAW_URL
 * @param model - 
 */
export const PatchJavLibraryOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/javlibrary'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const PatchJavLibraryOptions_RAW_URL = function() {
  return '/options/javlibrary'
}
export const PatchJavLibraryOptions_TYPE = function() {
  return 'patch'
}
export const PatchJavLibraryOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/javlibrary'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetPixivOptions
 * url: GetPixivOptionsURL
 * method: GetPixivOptions_TYPE
 * raw_url: GetPixivOptions_RAW_URL
 */
export const GetPixivOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/pixiv'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetPixivOptions_RAW_URL = function() {
  return '/options/pixiv'
}
export const GetPixivOptions_TYPE = function() {
  return 'get'
}
export const GetPixivOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/pixiv'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchPixivOptions
 * url: PatchPixivOptionsURL
 * method: PatchPixivOptions_TYPE
 * raw_url: PatchPixivOptions_RAW_URL
 * @param model - 
 */
export const PatchPixivOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/pixiv'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const PatchPixivOptions_RAW_URL = function() {
  return '/options/pixiv'
}
export const PatchPixivOptions_TYPE = function() {
  return 'patch'
}
export const PatchPixivOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/pixiv'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetResourceOptions
 * url: GetResourceOptionsURL
 * method: GetResourceOptions_TYPE
 * raw_url: GetResourceOptions_RAW_URL
 */
export const GetResourceOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/resource'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetResourceOptions_RAW_URL = function() {
  return '/options/resource'
}
export const GetResourceOptions_TYPE = function() {
  return 'get'
}
export const GetResourceOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/resource'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchResourceOptions
 * url: PatchResourceOptionsURL
 * method: PatchResourceOptions_TYPE
 * raw_url: PatchResourceOptions_RAW_URL
 * @param model - 
 */
export const PatchResourceOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/resource'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const PatchResourceOptions_RAW_URL = function() {
  return '/options/resource'
}
export const PatchResourceOptions_TYPE = function() {
  return 'patch'
}
export const PatchResourceOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/resource'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetThirdPartyOptions
 * url: GetThirdPartyOptionsURL
 * method: GetThirdPartyOptions_TYPE
 * raw_url: GetThirdPartyOptions_RAW_URL
 */
export const GetThirdPartyOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/thirdparty'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetThirdPartyOptions_RAW_URL = function() {
  return '/options/thirdparty'
}
export const GetThirdPartyOptions_TYPE = function() {
  return 'get'
}
export const GetThirdPartyOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/thirdparty'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchThirdPartyOptions
 * url: PatchThirdPartyOptionsURL
 * method: PatchThirdPartyOptions_TYPE
 * raw_url: PatchThirdPartyOptions_RAW_URL
 * @param model - 
 */
export const PatchThirdPartyOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/thirdparty'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const PatchThirdPartyOptions_RAW_URL = function() {
  return '/options/thirdparty'
}
export const PatchThirdPartyOptions_TYPE = function() {
  return 'patch'
}
export const PatchThirdPartyOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/thirdparty'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetNetworkOptions
 * url: GetNetworkOptionsURL
 * method: GetNetworkOptions_TYPE
 * raw_url: GetNetworkOptions_RAW_URL
 */
export const GetNetworkOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/network'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetNetworkOptions_RAW_URL = function() {
  return '/options/network'
}
export const GetNetworkOptions_TYPE = function() {
  return 'get'
}
export const GetNetworkOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/network'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchNetworkOptions
 * url: PatchNetworkOptionsURL
 * method: PatchNetworkOptions_TYPE
 * raw_url: PatchNetworkOptions_RAW_URL
 * @param model - 
 */
export const PatchNetworkOptions = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/options/network'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('patch', domain + path, body, queryParameters, form, config)
}
export const PatchNetworkOptions_RAW_URL = function() {
  return '/options/network'
}
export const PatchNetworkOptions_TYPE = function() {
  return 'patch'
}
export const PatchNetworkOptionsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/options/network'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: SearchPasswords
 * url: SearchPasswordsURL
 * method: SearchPasswords_TYPE
 * raw_url: SearchPasswords_RAW_URL
 * @param order - 
 * @param pageIndex - 
 * @param pageSize - 
 * @param skipCount - 
 */
export const SearchPasswords = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/password'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['order'] !== undefined) {
    queryParameters['order'] = parameters['order']
  }
  if (parameters['pageIndex'] !== undefined) {
    queryParameters['pageIndex'] = parameters['pageIndex']
  }
  if (parameters['pageSize'] !== undefined) {
    queryParameters['pageSize'] = parameters['pageSize']
  }
  if (parameters['skipCount'] !== undefined) {
    queryParameters['skipCount'] = parameters['skipCount']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const SearchPasswords_RAW_URL = function() {
  return '/password'
}
export const SearchPasswords_TYPE = function() {
  return 'get'
}
export const SearchPasswordsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/password'
  if (parameters['order'] !== undefined) {
    queryParameters['order'] = parameters['order']
  }
  if (parameters['pageIndex'] !== undefined) {
    queryParameters['pageIndex'] = parameters['pageIndex']
  }
  if (parameters['pageSize'] !== undefined) {
    queryParameters['pageSize'] = parameters['pageSize']
  }
  if (parameters['skipCount'] !== undefined) {
    queryParameters['skipCount'] = parameters['skipCount']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllPasswords
 * url: GetAllPasswordsURL
 * method: GetAllPasswords_TYPE
 * raw_url: GetAllPasswords_RAW_URL
 */
export const GetAllPasswords = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/password/all'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllPasswords_RAW_URL = function() {
  return '/password/all'
}
export const GetAllPasswords_TYPE = function() {
  return 'get'
}
export const GetAllPasswordsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/password/all'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: DeletePassword
 * url: DeletePasswordURL
 * method: DeletePassword_TYPE
 * raw_url: DeletePassword_RAW_URL
 * @param password - 
 */
export const DeletePassword = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/password/{password}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{password}', `${parameters['password']}`)
  if (parameters['password'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: password'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const DeletePassword_RAW_URL = function() {
  return '/password/{password}'
}
export const DeletePassword_TYPE = function() {
  return 'delete'
}
export const DeletePasswordURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/password/{password}'
  path = path.replace('{password}', `${parameters['password']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetPlaylist
 * url: GetPlaylistURL
 * method: GetPlaylist_TYPE
 * raw_url: GetPlaylist_RAW_URL
 * @param id - 
 */
export const GetPlaylist = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/playlist/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetPlaylist_RAW_URL = function() {
  return '/playlist/{id}'
}
export const GetPlaylist_TYPE = function() {
  return 'get'
}
export const GetPlaylistURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/playlist/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: DeletePlaylist
 * url: DeletePlaylistURL
 * method: DeletePlaylist_TYPE
 * raw_url: DeletePlaylist_RAW_URL
 * @param id - 
 */
export const DeletePlaylist = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/playlist/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const DeletePlaylist_RAW_URL = function() {
  return '/playlist/{id}'
}
export const DeletePlaylist_TYPE = function() {
  return 'delete'
}
export const DeletePlaylistURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/playlist/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllPlaylists
 * url: GetAllPlaylistsURL
 * method: GetAllPlaylists_TYPE
 * raw_url: GetAllPlaylists_RAW_URL
 */
export const GetAllPlaylists = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/playlist'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllPlaylists_RAW_URL = function() {
  return '/playlist'
}
export const GetAllPlaylists_TYPE = function() {
  return 'get'
}
export const GetAllPlaylistsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/playlist'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: AddPlaylist
 * url: AddPlaylistURL
 * method: AddPlaylist_TYPE
 * raw_url: AddPlaylist_RAW_URL
 * @param model - 
 */
export const AddPlaylist = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/playlist'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const AddPlaylist_RAW_URL = function() {
  return '/playlist'
}
export const AddPlaylist_TYPE = function() {
  return 'post'
}
export const AddPlaylistURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/playlist'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PutPlaylist
 * url: PutPlaylistURL
 * method: PutPlaylist_TYPE
 * raw_url: PutPlaylist_RAW_URL
 * @param model - 
 */
export const PutPlaylist = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/playlist'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const PutPlaylist_RAW_URL = function() {
  return '/playlist'
}
export const PutPlaylist_TYPE = function() {
  return 'put'
}
export const PutPlaylistURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/playlist'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetPlaylistFiles
 * url: GetPlaylistFilesURL
 * method: GetPlaylistFiles_TYPE
 * raw_url: GetPlaylistFiles_RAW_URL
 * @param id - 
 */
export const GetPlaylistFiles = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/playlist/{id}/files'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetPlaylistFiles_RAW_URL = function() {
  return '/playlist/{id}/files'
}
export const GetPlaylistFiles_TYPE = function() {
  return 'get'
}
export const GetPlaylistFilesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/playlist/{id}/files'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetResourceSearchCriteria
 * url: GetResourceSearchCriteriaURL
 * method: GetResourceSearchCriteria_TYPE
 * raw_url: GetResourceSearchCriteria_RAW_URL
 */
export const GetResourceSearchCriteria = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/search-criteria'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetResourceSearchCriteria_RAW_URL = function() {
  return '/resource/search-criteria'
}
export const GetResourceSearchCriteria_TYPE = function() {
  return 'get'
}
export const GetResourceSearchCriteriaURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/search-criteria'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: SearchResources
 * url: SearchResourcesURL
 * method: SearchResources_TYPE
 * raw_url: SearchResources_RAW_URL
 * @param model - 
 */
export const SearchResources = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/search'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const SearchResources_RAW_URL = function() {
  return '/resource/search'
}
export const SearchResources_TYPE = function() {
  return 'post'
}
export const SearchResourcesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/search'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetResourcesByKeys
 * url: GetResourcesByKeysURL
 * method: GetResourcesByKeys_TYPE
 * raw_url: GetResourcesByKeys_RAW_URL
 * @param ids - 
 * @param additionalItems - 
 */
export const GetResourcesByKeys = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/keys'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['ids'] !== undefined) {
    queryParameters['ids'] = parameters['ids']
  }
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetResourcesByKeys_RAW_URL = function() {
  return '/resource/keys'
}
export const GetResourcesByKeys_TYPE = function() {
  return 'get'
}
export const GetResourcesByKeysURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/keys'
  if (parameters['ids'] !== undefined) {
    queryParameters['ids'] = parameters['ids']
  }
  if (parameters['additionalItems'] !== undefined) {
    queryParameters['additionalItems'] = parameters['additionalItems']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: OpenResourceDirectory
 * url: OpenResourceDirectoryURL
 * method: OpenResourceDirectory_TYPE
 * raw_url: OpenResourceDirectory_RAW_URL
 * @param id - 
 */
export const OpenResourceDirectory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/directory'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['id'] !== undefined) {
    queryParameters['id'] = parameters['id']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const OpenResourceDirectory_RAW_URL = function() {
  return '/resource/directory'
}
export const OpenResourceDirectory_TYPE = function() {
  return 'get'
}
export const OpenResourceDirectoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/directory'
  if (parameters['id'] !== undefined) {
    queryParameters['id'] = parameters['id']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetResourceCover
 * url: GetResourceCoverURL
 * method: GetResourceCover_TYPE
 * raw_url: GetResourceCover_RAW_URL
 * @param id - 
 */
export const GetResourceCover = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/{id}/cover'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetResourceCover_RAW_URL = function() {
  return '/resource/{id}/cover'
}
export const GetResourceCover_TYPE = function() {
  return 'get'
}
export const GetResourceCoverURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/{id}/cover'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: SaveCover
 * url: SaveCoverURL
 * method: SaveCover_TYPE
 * raw_url: SaveCover_RAW_URL
 * @param id - 
 * @param model - 
 */
export const SaveCover = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/{id}/cover'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const SaveCover_RAW_URL = function() {
  return '/resource/{id}/cover'
}
export const SaveCover_TYPE = function() {
  return 'post'
}
export const SaveCoverURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/{id}/cover'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetResourcePlayableFiles
 * url: GetResourcePlayableFilesURL
 * method: GetResourcePlayableFiles_TYPE
 * raw_url: GetResourcePlayableFiles_RAW_URL
 * @param id - 
 */
export const GetResourcePlayableFiles = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/{id}/playable-files'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetResourcePlayableFiles_RAW_URL = function() {
  return '/resource/{id}/playable-files'
}
export const GetResourcePlayableFiles_TYPE = function() {
  return 'get'
}
export const GetResourcePlayableFilesURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/{id}/playable-files'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: ClearResourceTask
 * url: ClearResourceTaskURL
 * method: ClearResourceTask_TYPE
 * raw_url: ClearResourceTask_RAW_URL
 * @param id - 
 */
export const ClearResourceTask = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/{id}/task'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const ClearResourceTask_RAW_URL = function() {
  return '/resource/{id}/task'
}
export const ClearResourceTask_TYPE = function() {
  return 'delete'
}
export const ClearResourceTaskURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/{id}/task'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetResourceDataForPreviewer
 * url: GetResourceDataForPreviewerURL
 * method: GetResourceDataForPreviewer_TYPE
 * raw_url: GetResourceDataForPreviewer_RAW_URL
 * @param id - 
 */
export const GetResourceDataForPreviewer = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/{id}/previewer'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetResourceDataForPreviewer_RAW_URL = function() {
  return '/resource/{id}/previewer'
}
export const GetResourceDataForPreviewer_TYPE = function() {
  return 'get'
}
export const GetResourceDataForPreviewerURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/{id}/previewer'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PutResourcePropertyValue
 * url: PutResourcePropertyValueURL
 * method: PutResourcePropertyValue_TYPE
 * raw_url: PutResourcePropertyValue_RAW_URL
 * @param id - 
 * @param model - 
 */
export const PutResourcePropertyValue = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/{id}/property-value'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const PutResourcePropertyValue_RAW_URL = function() {
  return '/resource/{id}/property-value'
}
export const PutResourcePropertyValue_TYPE = function() {
  return 'put'
}
export const PutResourcePropertyValueURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/{id}/property-value'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PlayResourceFile
 * url: PlayResourceFileURL
 * method: PlayResourceFile_TYPE
 * raw_url: PlayResourceFile_RAW_URL
 * @param resourceId - 
 * @param file - 
 */
export const PlayResourceFile = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/resource/{resourceId}/play'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{resourceId}', `${parameters['resourceId']}`)
  if (parameters['resourceId'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: resourceId'))
  }
  if (parameters['file'] !== undefined) {
    queryParameters['file'] = parameters['file']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const PlayResourceFile_RAW_URL = function() {
  return '/resource/{resourceId}/play'
}
export const PlayResourceFile_TYPE = function() {
  return 'get'
}
export const PlayResourceFileURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/resource/{resourceId}/play'
  path = path.replace('{resourceId}', `${parameters['resourceId']}`)
  if (parameters['file'] !== undefined) {
    queryParameters['file'] = parameters['file']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllSpecialTexts
 * url: GetAllSpecialTextsURL
 * method: GetAllSpecialTexts_TYPE
 * raw_url: GetAllSpecialTexts_RAW_URL
 */
export const GetAllSpecialTexts = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/special-text'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllSpecialTexts_RAW_URL = function() {
  return '/special-text'
}
export const GetAllSpecialTexts_TYPE = function() {
  return 'get'
}
export const GetAllSpecialTextsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/special-text'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: AddSpecialText
 * url: AddSpecialTextURL
 * method: AddSpecialText_TYPE
 * raw_url: AddSpecialText_RAW_URL
 * @param model - 
 */
export const AddSpecialText = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/special-text'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const AddSpecialText_RAW_URL = function() {
  return '/special-text'
}
export const AddSpecialText_TYPE = function() {
  return 'post'
}
export const AddSpecialTextURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/special-text'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: DeleteSpecialText
 * url: DeleteSpecialTextURL
 * method: DeleteSpecialText_TYPE
 * raw_url: DeleteSpecialText_RAW_URL
 * @param id - 
 */
export const DeleteSpecialText = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/special-text/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const DeleteSpecialText_RAW_URL = function() {
  return '/special-text/{id}'
}
export const DeleteSpecialText_TYPE = function() {
  return 'delete'
}
export const DeleteSpecialTextURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/special-text/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PatchSpecialText
 * url: PatchSpecialTextURL
 * method: PatchSpecialText_TYPE
 * raw_url: PatchSpecialText_RAW_URL
 * @param id - 
 * @param model - 
 */
export const PatchSpecialText = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/special-text/{id}'
  let body
  let queryParameters = {}
  let form = {}
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters['id'] === undefined) {
    return Promise.reject(new Error('Missing required  parameter: id'))
  }
  if (parameters['model'] !== undefined) {
    body = parameters['model']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('put', domain + path, body, queryParameters, form, config)
}
export const PatchSpecialText_RAW_URL = function() {
  return '/special-text/{id}'
}
export const PatchSpecialText_TYPE = function() {
  return 'put'
}
export const PatchSpecialTextURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/special-text/{id}'
  path = path.replace('{id}', `${parameters['id']}`)
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: AddSpecialTextPrefabs
 * url: AddSpecialTextPrefabsURL
 * method: AddSpecialTextPrefabs_TYPE
 * raw_url: AddSpecialTextPrefabs_RAW_URL
 */
export const AddSpecialTextPrefabs = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/special-text/prefabs'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const AddSpecialTextPrefabs_RAW_URL = function() {
  return '/special-text/prefabs'
}
export const AddSpecialTextPrefabs_TYPE = function() {
  return 'post'
}
export const AddSpecialTextPrefabsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/special-text/prefabs'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: PretreatText
 * url: PretreatTextURL
 * method: PretreatText_TYPE
 * raw_url: PretreatText_RAW_URL
 * @param text - 
 */
export const PretreatText = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/special-text/pretreatment'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['text'] !== undefined) {
    queryParameters['text'] = parameters['text']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const PretreatText_RAW_URL = function() {
  return '/special-text/pretreatment'
}
export const PretreatText_TYPE = function() {
  return 'post'
}
export const PretreatTextURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/special-text/pretreatment'
  if (parameters['text'] !== undefined) {
    queryParameters['text'] = parameters['text']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetAllThirdPartyRequestStatistics
 * url: GetAllThirdPartyRequestStatisticsURL
 * method: GetAllThirdPartyRequestStatistics_TYPE
 * raw_url: GetAllThirdPartyRequestStatistics_RAW_URL
 */
export const GetAllThirdPartyRequestStatistics = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/third-party/request-statistics'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetAllThirdPartyRequestStatistics_RAW_URL = function() {
  return '/third-party/request-statistics'
}
export const GetAllThirdPartyRequestStatistics_TYPE = function() {
  return 'get'
}
export const GetAllThirdPartyRequestStatisticsURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/third-party/request-statistics'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: OpenFileOrDirectory
 * url: OpenFileOrDirectoryURL
 * method: OpenFileOrDirectory_TYPE
 * raw_url: OpenFileOrDirectory_RAW_URL
 * @param path - 
 * @param openInDirectory - 
 */
export const OpenFileOrDirectory = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/tool/open'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters['openInDirectory'] !== undefined) {
    queryParameters['openInDirectory'] = parameters['openInDirectory']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const OpenFileOrDirectory_RAW_URL = function() {
  return '/tool/open'
}
export const OpenFileOrDirectory_TYPE = function() {
  return 'get'
}
export const OpenFileOrDirectoryURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/tool/open'
  if (parameters['path'] !== undefined) {
    queryParameters['path'] = parameters['path']
  }
  if (parameters['openInDirectory'] !== undefined) {
    queryParameters['openInDirectory'] = parameters['openInDirectory']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * Test
 * request: getToolTest
 * url: getToolTestURL
 * method: getToolTest_TYPE
 * raw_url: getToolTest_RAW_URL
 */
export const getToolTest = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/tool/test'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const getToolTest_RAW_URL = function() {
  return '/tool/test'
}
export const getToolTest_TYPE = function() {
  return 'get'
}
export const getToolTestURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/tool/test'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: ValidateCookie
 * url: ValidateCookieURL
 * method: ValidateCookie_TYPE
 * raw_url: ValidateCookie_RAW_URL
 * @param target - 
 * @param cookie - 
 */
export const ValidateCookie = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/tool/cookie-validation'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters['target'] !== undefined) {
    queryParameters['target'] = parameters['target']
  }
  if (parameters['cookie'] !== undefined) {
    queryParameters['cookie'] = parameters['cookie']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const ValidateCookie_RAW_URL = function() {
  return '/tool/cookie-validation'
}
export const ValidateCookie_TYPE = function() {
  return 'get'
}
export const ValidateCookieURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/tool/cookie-validation'
  if (parameters['target'] !== undefined) {
    queryParameters['target'] = parameters['target']
  }
  if (parameters['cookie'] !== undefined) {
    queryParameters['cookie'] = parameters['cookie']
  }
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: GetNewAppVersion
 * url: GetNewAppVersionURL
 * method: GetNewAppVersion_TYPE
 * raw_url: GetNewAppVersion_RAW_URL
 */
export const GetNewAppVersion = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/updater/app/new-version'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('get', domain + path, body, queryParameters, form, config)
}
export const GetNewAppVersion_RAW_URL = function() {
  return '/updater/app/new-version'
}
export const GetNewAppVersion_TYPE = function() {
  return 'get'
}
export const GetNewAppVersionURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/updater/app/new-version'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StartUpdatingApp
 * url: StartUpdatingAppURL
 * method: StartUpdatingApp_TYPE
 * raw_url: StartUpdatingApp_RAW_URL
 */
export const StartUpdatingApp = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/updater/app/update'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const StartUpdatingApp_RAW_URL = function() {
  return '/updater/app/update'
}
export const StartUpdatingApp_TYPE = function() {
  return 'post'
}
export const StartUpdatingAppURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/updater/app/update'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: StopUpdatingApp
 * url: StopUpdatingAppURL
 * method: StopUpdatingApp_TYPE
 * raw_url: StopUpdatingApp_RAW_URL
 */
export const StopUpdatingApp = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/updater/app/update'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('delete', domain + path, body, queryParameters, form, config)
}
export const StopUpdatingApp_RAW_URL = function() {
  return '/updater/app/update'
}
export const StopUpdatingApp_TYPE = function() {
  return 'delete'
}
export const StopUpdatingAppURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/updater/app/update'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}
/**
 * 
 * request: RestartAndUpdateApp
 * url: RestartAndUpdateAppURL
 * method: RestartAndUpdateApp_TYPE
 * raw_url: RestartAndUpdateApp_RAW_URL
 */
export const RestartAndUpdateApp = function(parameters = {}) {
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  const config = parameters.$config
  let path = '/updater/app/restart'
  let body
  let queryParameters = {}
  let form = {}
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    });
  }
  return request('post', domain + path, body, queryParameters, form, config)
}
export const RestartAndUpdateApp_RAW_URL = function() {
  return '/updater/app/restart'
}
export const RestartAndUpdateApp_TYPE = function() {
  return 'post'
}
export const RestartAndUpdateAppURL = function(parameters = {}) {
  let queryParameters = {}
  const domain = parameters.$domain ? parameters.$domain : getDomain()
  let path = '/updater/app/restart'
  if (parameters.$queryParameters) {
    Object.keys(parameters.$queryParameters).forEach(function(parameterName) {
      queryParameters[parameterName] = parameters.$queryParameters[parameterName]
    })
  }
  let keys = Object.keys(queryParameters)
  return domain + path + (keys.length > 0 ? '?' + (keys.map(key => key + '=' + encodeURIComponent(queryParameters[key])).join('&')) : '')
}