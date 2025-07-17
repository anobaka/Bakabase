import React from 'react';
import { Tabs, Tab } from '@/components/bakaui';

export default () => {
    // 生成很多tab来测试超长情况
    const generateTabs = () => {
        const tabs: React.ReactElement[] = [];
        for (let i = 1; i <= 50; i++) {
            tabs.push(
                <Tab 
                    key={`tab-${i}`} 
                    title={`Tab ${i} - This is a very long tab title to test how HeroUI handles long text in tabs`}
                >
                    <div className="p-4">
                        <h3>Content for Tab {i}</h3>
                        <p>This is the content for tab number {i}. This tab has a very long title to test how the UI handles overflow.</p>
                        <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.</p>
                    </div>
                </Tab>
            );
        }
        return tabs;
    };

    return (
        <div className="w-full space-y-8">
            <h2 className="mb-4">Testing HeroUI Tabs with Many Long Titles</h2>
            <p className="mb-4 text-sm text-default-500">
                This test case creates 50 tabs with very long titles to test how HeroUI handles overflow and scrolling behavior.
            </p>
            
            {/* 测试1: 默认样式 - 可能超出屏幕 */}
            <div>
                <h3 className="mb-2">Test 1: Default Style (may overflow)</h3>
                <Tabs 
                    aria-label="Long tabs test - default" 
                    variant="underlined"
                    className="w-full"
                    size="lg"
                >
                    {generateTabs()}
                </Tabs>
            </div>

            {/* 测试2: 带水平滚动的容器 */}
            <div>
                <h3 className="mb-2">Test 2: With Horizontal Scroll Container</h3>
                <div className="overflow-x-auto">
                    <Tabs 
                        aria-label="Long tabs test - scrollable" 
                        variant="underlined"
                        className="w-max min-w-full"
                        size="lg"
                    >
                        {generateTabs()}
                    </Tabs>
                </div>
            </div>

            {/* 测试3: 使用solid变体 */}
            <div>
                <h3 className="mb-2">Test 3: Solid Variant</h3>
                <Tabs 
                    aria-label="Long tabs test - solid" 
                    variant="solid"
                    className="w-full"
                    size="lg"
                >
                    {generateTabs()}
                </Tabs>
            </div>

            {/* 测试4: 使用bordered变体 */}
            <div>
                <h3 className="mb-2">Test 4: Bordered Variant</h3>
                <Tabs 
                    aria-label="Long tabs test - bordered" 
                    variant="bordered"
                    className="w-full"
                    size="lg"
                >
                    {generateTabs()}
                </Tabs>
            </div>

            {/* 测试5: 使用light变体 */}
            <div>
                <h3 className="mb-2">Test 5: Light Variant</h3>
                <Tabs 
                    aria-label="Long tabs test - light" 
                    variant="light"
                    className="w-full"
                    size="lg"
                >
                    {generateTabs()}
                </Tabs>
            </div>

            {/* 测试6: 使用small尺寸 */}
            <div>
                <h3 className="mb-2">Test 6: Small Size</h3>
                <Tabs 
                    aria-label="Long tabs test - small" 
                    variant="underlined"
                    className="w-full"
                    size="sm"
                >
                    {generateTabs()}
                </Tabs>
            </div>

            {/* 测试7: 使用medium尺寸 */}
            <div>
                <h3 className="mb-2">Test 7: Medium Size</h3>
                <Tabs 
                    aria-label="Long tabs test - medium" 
                    variant="underlined"
                    className="w-full"
                    size="md"
                >
                    {generateTabs()}
                </Tabs>
            </div>
        </div>
    );
};