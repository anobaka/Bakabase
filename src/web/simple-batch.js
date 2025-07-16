const fs = require('fs');

// 读取所有.tsx文件
const files = [
  'app/alias/page.tsx',
  'app/background-task/page.tsx', 
  'app/bulk-modification/page.tsx',
  'app/bulk-modification2/page.tsx',
  'app/cache/page.tsx',
  'app/category/page.tsx',
  'app/configuration/page.tsx',
  'app/custom-component/page.tsx',
  'app/custom-property/page.tsx',
  'app/dashboard/page.tsx',
  'app/downloader/page.tsx',
  'app/extension-group/page.tsx',
  'app/file-mover/page.tsx',
  'app/file-name-modifier/page.tsx',
  'app/log/page.tsx',
  'app/media-library/page.tsx',
  'app/media-library-template/page.tsx',
  'app/migration/page.tsx',
  'app/play-history/page.tsx',
  'app/post-parser/page.tsx',
  'app/synchronization-options/page.tsx',
  'app/test/page.tsx',
  'app/text/page.tsx',
  'app/third-party-integration/page.tsx',
  'app/welcome/page.tsx',
  'components/AnimatedArrow/index.tsx',
  'components/BlockSort/index.tsx',
  'components/BRjsf/index.tsx',
  'components/ClickableIcon/index.tsx',
  'components/ColorPicker/index.tsx',
  'components/CustomIcon.tsx',
  'components/DurationInput/index.tsx',
  'components/EditableTree/index.tsx',
  'components/EditableValue/index.tsx',
  'components/Enhancer/index.tsx',
  'components/ExtensionGroupSelect/index.tsx',
  'components/ExtensionsInput/index.tsx',
  'components/ExternalLink/index.tsx',
  'components/FeatureStatusTip/index.tsx',
  'components/FileNameModifier/index.tsx',
  'components/FileNameModifierModal/index.tsx',
  'components/FileSystemEntryIcon/index.tsx',
  'components/FloatingAssistant/index.tsx',
  'components/HandleUnknownResources/index.tsx',
  'components/Labels/index.tsx',
  'components/MediaLibraryPathSelectorV2/index.tsx',
  'components/MediaLibrarySelectorV2/index.tsx',
  'components/MediaPreviewer/index.tsx',
  'components/PasswordSelector/index.tsx',
  'components/PathSegmentsConfiguration/index.tsx',
  'components/Playlist/Collection/index.tsx',
  'components/Playlist/Detail/index.tsx',
  'components/ProgressorHubConnection/index.tsx',
  'components/PropertiesMatcher/index.tsx',
  'components/Property/index.tsx',
  'components/PropertyMatcher/index.tsx',
  'components/PropertyModal/index.tsx',
  'components/PropertySelector/index.tsx',
  'components/ResourcesModal/index.tsx',
  'components/ResourceTransferModal/index.tsx',
  'components/SimpleLabel/index.tsx',
  'components/SimpleOneStepDialog/index.tsx',
  'components/SpecialText/index.tsx',
  'components/StandardValue/index.tsx',
  'components/TextReader/index.tsx',
  'components/TextValueSplitedBySpaceEditor/index.tsx',
  'components/ThirdPartyIcon/index.tsx',
  'components/TimeRanger/index.tsx',
  'components/Title/index.tsx',
  'layouts/BasicLayout/index.tsx',
  'layouts/BlankLayout/index.tsx'
];

let processed = 0;
let skipped = 0;

files.forEach(file => {
  try {
    if (!fs.existsSync(file)) {
      console.log(`跳过: ${file} (不存在)`);
      return;
    }
    
    const content = fs.readFileSync(file, 'utf8');
    
    // 检查是否已有 'use client'
    if (content.includes("'use client'") || content.includes('"use client"')) {
      skipped++;
      return;
    }
    
    // 检查是否需要 'use client'
    const hasHooks = content.match(/use[A-Z][a-zA-Z]*\s*\(/);
    const hasBrowserAPI = content.match(/\b(window|document|localStorage|sessionStorage)\./);
    const hasEventHandlers = content.match(/on[A-Z][a-zA-Z]*\s*=/);
    
    if (hasHooks || hasBrowserAPI || hasEventHandlers) {
      const newContent = `'use client';\n\n${content}`;
      fs.writeFileSync(file, newContent, 'utf8');
      processed++;
      console.log(`已处理: ${file}`);
    } else {
      skipped++;
    }
  } catch (error) {
    console.log(`错误: ${file} - ${error.message}`);
  }
});

console.log(`\n完成! 处理: ${processed}, 跳过: ${skipped}`); 