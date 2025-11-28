// ==UserScript==
// @name         Bakabase 第三方内容追踪器
// @namespace    http://tampermonkey.net/
// @version      1.0.0
// @description  追踪第三方网站的浏览记录，标记已看过和有更新的内容
// @author       Bakabase
// @match        *://*/*
// @grant        GM_xmlhttpRequest
// @grant        GM_getValue
// @grant        GM_setValue
// @run-at       document-end
// @connect      localhost
// @connect      127.0.0.1
// ==/UserScript==

(function() {
    'use strict';

    // ==================== 使用说明 ====================
    // 
    // 标记显示规则：
    // 1. 未看过：不显示任何标记
    // 2. 已看过（无更新）：显示绿色 "✓" 标记，半透明，不影响阅读
    // 3. 有更新：显示橙色 "更新" 标记，稍微醒目一些
    //
    // 判断"有更新"的逻辑：
    // - 内容有 updatedAt 时间
    // - 且这个时间比上次查看时记录的 updatedAt 要晚
    // - 后端 API 会负责比较并返回 hasUpdate 标志
    //
    // 工作流程：
    // 1. 页面加载时，提取所有内容 ID
    // 2. 查询这些 ID 的状态（已看过？有更新？）
    // 3. 用户滚动时，检查视口中的内容
    // 4. 自动标记视口中可见但未标记的内容
    // 5. 渲染标记到页面上
    //
    // ==================== 配置部分 ====================

    // API 基础地址（需要根据实际部署修改）
    const API_BASE_URL = GM_getValue('api_base_url', '{appEndpoint}');

    // 网站配置
    const SITE_CONFIGS = {
        'exhentai': {
            key: 'exhentai',
            domains: ['exhentai.org', 'github.com'],

            // 从 URL 提取 filter（例如从搜索关键词、标签等）
            extractFilter: (url) => {
                return null;
            },

            // 提取页面上的内容 ID 列表
            extractContentIds: (document) => {
              const texts = [...new Set(
                Array.from(document.querySelectorAll(
                  'ul[data-filterable-for="your-repos-filter"] > li > div > div > h3 > a'
                )).map(link => link.innerText)
              )];
              // console.log('extracted content ids:', texts);
              return texts;
            },

            // 提取内容的更新时间
            extractUpdateTime: (element) => {
                // 根据网站的实际结构提取更新时间
                // 返回 Date 对象或 null
                // 示例：
                // const timeElement = element.querySelector('.update-time');
                // if (timeElement) {
                //     const timeText = timeElement.textContent;
                //     return new Date(timeText);
                // }
                return null;
            },

            // 根据内容 ID 查找对应的 DOM 元素
            findElementByContentId: (contentId) => {
                // 根据网站的实际结构查找元素
                // 示例：GitHub 仓库列表
                const links = document.querySelectorAll('ul[data-filterable-for="your-repos-filter"] > li > div > div > h3 > a');
                for (const link of links) {
                    if (link.innerText === contentId) {
                        // 返回包含该内容的容器元素
                        return link.closest('li');
                    }
                }
                return null;
            },

            // 标记为已看过的事件
            onMarkViewed: () => {
                // 滚动时自动标记视口中可见的内容
                let markTimeout;
                window.addEventListener('scroll', () => {
                    // 使用防抖，避免频繁标记
                    clearTimeout(markTimeout);
                    markTimeout = setTimeout(() => {
                        markVisibleContentAsViewed();
                    }, 500);
                });
                
                // 页面加载后也检查一次
                setTimeout(() => markVisibleContentAsViewed(), 1000);
            },

            // 自定义渲染标记
            renderMarker: (element, status) => {
                // element: 内容对应的 DOM 元素
                // status: { isViewed, hasUpdate, viewedAt, updatedAt }

                // 移除旧标记
                const oldMarker = element.querySelector('.bakabase-marker');
                if (oldMarker) {
                    oldMarker.remove();
                }

                // 未看过不显示标记
                if (!status.isViewed) {
                    return;
                }

                // 创建标记元素
                const marker = document.createElement('div');
                marker.className = 'bakabase-marker';
                
                // 基础样式（更小巧，不影响主内容）
                marker.style.cssText = `
                    position: absolute;
                    top: 2px;
                    right: 2px;
                    padding: 2px 6px;
                    border-radius: 3px;
                    font-size: 10px;
                    font-weight: normal;
                    z-index: 100;
                    opacity: 0.7;
                    transition: opacity 0.2s;
                    pointer-events: none;
                `;

                // 根据状态设置样式
                if (status.hasUpdate) {
                    // 有更新：橙色背景，更醒目
                    marker.textContent = '更新';
                    marker.style.backgroundColor = '#ff9800';
                    marker.style.color = '#fff';
                    marker.style.opacity = '0.85';
                } else {
                    // 已看过：绿色背景，更低调
                    marker.textContent = '✓';
                    marker.style.backgroundColor = '#4caf50';
                    marker.style.color = '#fff';
                    marker.style.opacity = '0.6';
                }

                // 确保父元素有 position: relative
                if (getComputedStyle(element).position === 'static') {
                    element.style.position = 'relative';
                }
                
                // 鼠标悬停时稍微提高不透明度，便于查看
                element.addEventListener('mouseenter', () => {
                    marker.style.opacity = '1';
                });
                element.addEventListener('mouseleave', () => {
                    marker.style.opacity = status.hasUpdate ? '0.85' : '0.6';
                });
                
                element.appendChild(marker);
            }
        },

        // 可以继续添加其他网站的配置...
    };

    // ==================== 核心逻辑 ====================

    let currentSiteConfig = null;
    let currentContentIds = [];
    let contentStatusMap = new Map();

    // 初始化
    function init() {
        // 检测当前网站
        const hostname = window.location.hostname;
        for (const [key, config] of Object.entries(SITE_CONFIGS)) {
            if (config.domains.some(domain => hostname.includes(domain))) {
                currentSiteConfig = config;
                console.log(`[Bakabase] 检测到网站: ${config.key}`);
                break;
            }
        }

        if (!currentSiteConfig) {
            return; // 不支持的网站
        }

        // 启动追踪
        startTracking();
    }

    // 启动追踪
    function startTracking() {
        // 提取当前页面的内容
        extractAndQueryContent();

        // 设置标记已看过的事件
        if (currentSiteConfig.onMarkViewed) {
            currentSiteConfig.onMarkViewed();
        }

        // 监听滚动事件
        let scrollTimeout;
        window.addEventListener('scroll', () => {
            // 使用防抖，避免频繁触发
            clearTimeout(scrollTimeout);
            scrollTimeout = setTimeout(() => {
                extractAndQueryContent();
            }, 300);
        });
    }

    // 提取并查询内容状态
    function extractAndQueryContent() {
        if (!currentSiteConfig) return;

        const contentIds = currentSiteConfig.extractContentIds(document);
        if (contentIds.length === 0) return;

        // 只查询新出现的内容（使用缓存过滤）
        const newIds = contentIds.filter(id => !contentStatusMap.has(id));
        
        console.log(`[Bakabase] 页面总内容数: ${contentIds.length}, 已缓存: ${contentIds.length - newIds.length}, 新内容: ${newIds.length}`);
        
        if (newIds.length === 0) {
            console.log('[Bakabase] 所有内容都已缓存，跳过查询');
            return;
        }

        currentContentIds = contentIds;

        // 仅查询新内容的状态
        queryContentStatus(newIds);
    }

    // 查询内容状态
    function queryContentStatus(contentIds) {
        const url = window.location.href;
        const filter = currentSiteConfig.extractFilter(url);

        console.log(`[Bakabase] 正在查询 ${contentIds.length} 个新内容的状态...`);

        GM_xmlhttpRequest({
            method: 'POST',
            url: `${API_BASE_URL}/third-party-content-tracker/query`,
            headers: {
                'Content-Type': 'application/json'
            },
            data: JSON.stringify({
                domainKey: currentSiteConfig.key,
                filter: filter,
                contentIds: contentIds
            }),
            onload: function(response) {
                if (response.status === 200) {
                    const result = JSON.parse(response.responseText);
                    if (result.data) {
                        // 更新状态映射（缓存查询结果）
                        let viewedCount = 0;
                        let updateCount = 0;
                        
                        result.data.forEach(item => {
                            contentStatusMap.set(item.contentId, {
                                isViewed: item.isViewed,
                                hasUpdate: item.hasUpdate,
                                viewedAt: item.viewedAt ? new Date(item.viewedAt) : null,
                                updatedAt: item.updatedAt ? new Date(item.updatedAt) : null
                            });
                            
                            if (item.isViewed) viewedCount++;
                            if (item.hasUpdate) updateCount++;
                        });

                        console.log(`[Bakabase] 查询完成: 总数 ${result.data.length}, 已看过 ${viewedCount}, 有更新 ${updateCount}, 缓存总数: ${contentStatusMap.size}`);

                        // 渲染标记
                        renderMarkers();
                    }
                }
            },
            onerror: function(error) {
                console.error('[Bakabase] 查询内容状态失败:', error);
            }
        });
    }

    // 渲染标记
    function renderMarkers() {
        if (!currentSiteConfig.renderMarker) return;
        if (!currentSiteConfig.findElementByContentId) {
            console.warn('[Bakabase] 网站配置未定义 findElementByContentId 方法');
            return;
        }

        // 重新提取当前页面的内容 ID
        const contentIds = currentSiteConfig.extractContentIds(document);
        
        let renderedCount = 0;
        let viewedCount = 0;
        let updateCount = 0;
        
        contentIds.forEach(contentId => {
            const status = contentStatusMap.get(contentId);
            if (status) {
                // 使用网站配置查找对应的 DOM 元素
                const element = currentSiteConfig.findElementByContentId(contentId);
                if (element) {
                    currentSiteConfig.renderMarker(element, status);
                    renderedCount++;
                    
                    if (status.hasUpdate) {
                        updateCount++;
                    } else if (status.isViewed) {
                        viewedCount++;
                    }
                }
            }
        });
        
        console.log(`[Bakabase] 渲染完成: 共 ${renderedCount} 个标记（已看过 ${viewedCount}, 有更新 ${updateCount}）`);
    }

    // 检查元素是否在视口中可见
    function isElementInViewport(element) {
        const rect = element.getBoundingClientRect();
        return (
            rect.top >= 0 &&
            rect.left >= 0 &&
            rect.bottom <= (window.innerHeight || document.documentElement.clientHeight) &&
            rect.right <= (window.innerWidth || document.documentElement.clientWidth)
        );
    }

    // 标记视口中可见的、未标记过的内容
    function markVisibleContentAsViewed() {
        if (!currentSiteConfig) return;

        // 提取当前页面的所有内容
        const allContentIds = currentSiteConfig.extractContentIds(document);
        if (allContentIds.length === 0) return;

        // 找出在视口中可见且未标记为已看过的内容
        const visibleUnviewedItems = [];
        
        allContentIds.forEach(contentId => {
            // 检查是否已经标记为已看过
            const status = contentStatusMap.get(contentId);
            if (status && status.isViewed) {
                return; // 已经标记过，跳过
            }

            // 使用网站配置查找对应的 DOM 元素
            if (!currentSiteConfig.findElementByContentId) {
                console.warn('[Bakabase] 网站配置未定义 findElementByContentId 方法');
                return;
            }

            const element = currentSiteConfig.findElementByContentId(contentId);
            if (element && isElementInViewport(element)) {
                // 提取更新时间
                const updatedAt = currentSiteConfig.extractUpdateTime ? 
                    currentSiteConfig.extractUpdateTime(element) : null;
                
                visibleUnviewedItems.push({
                    contentId: contentId,
                    element: element,
                    updatedAt: updatedAt
                });
            }
        });

        if (visibleUnviewedItems.length === 0) {
            console.log('[Bakabase] 视口中没有未标记的内容');
            return;
        }

        console.log(`[Bakabase] 发现视口中有 ${visibleUnviewedItems.length} 个未标记的内容，准备标记...`, 
            visibleUnviewedItems.map(item => item.contentId));

        // 标记这些内容
        const url = window.location.href;
        const filter = currentSiteConfig.extractFilter(url);

        const contentItems = visibleUnviewedItems.map(item => ({
            contentId: item.contentId,
            updatedAt: item.updatedAt ? item.updatedAt.toISOString() : null
        }));

        GM_xmlhttpRequest({
            method: 'POST',
            url: `${API_BASE_URL}/third-party-content-tracker/mark-viewed`,
            headers: {
                'Content-Type': 'application/json'
            },
            data: JSON.stringify({
                domainKey: currentSiteConfig.key,
                filter: filter,
                contentItems: contentItems
            }),
            onload: function(response) {
                if (response.status === 200) {
                    console.log(`[Bakabase] 成功标记 ${visibleUnviewedItems.length} 个内容为已看过`);
                    // 更新本地状态
                    visibleUnviewedItems.forEach(item => {
                        const contentId = item.contentId;
                        const updatedAt = item.updatedAt;
                        
                        if (!contentStatusMap.has(contentId)) {
                            contentStatusMap.set(contentId, {
                                isViewed: true,
                                hasUpdate: false,
                                viewedAt: new Date(),
                                updatedAt: updatedAt
                            });
                        } else {
                            const status = contentStatusMap.get(contentId);
                            status.isViewed = true;
                            status.viewedAt = new Date();
                            status.hasUpdate = false;
                            // 保存当前的 updatedAt，用于后续比较
                            if (updatedAt) {
                                status.updatedAt = updatedAt;
                            }
                        }
                    });
                    // 重新渲染标记
                    renderMarkers();
                }
            },
            onerror: function(error) {
                console.error('[Bakabase] 标记已看过失败:', error);
            }
        });
    }

    // 添加设置面板（可选）
    function addSettingsPanel() {
        // 创建一个浮动按钮用于打开设置
        const settingsBtn = document.createElement('button');
        settingsBtn.textContent = 'Bakabase 设置';
        settingsBtn.style.cssText = `
            position: fixed;
            bottom: 20px;
            right: 20px;
            padding: 10px 15px;
            background-color: #2196F3;
            color: white;
            border: none;
            border-radius: 5px;
            cursor: pointer;
            z-index: 10000;
            font-size: 14px;
        `;
        settingsBtn.onclick = () => {
            const newUrl = prompt('请输入 Bakabase API 地址:', API_BASE_URL);
            if (newUrl) {
                GM_setValue('api_base_url', newUrl);
                alert('设置已保存，请刷新页面生效');
            }
        };
        document.body.appendChild(settingsBtn);
    }

    // 启动脚本
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    // 添加设置面板
    addSettingsPanel();

})();
