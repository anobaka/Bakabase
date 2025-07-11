import React, { useState } from 'react';
import { Button, Notification } from '@/components/bakaui';
import FileNameModifierModal from '@/components/FileNameModifierModal';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

const FileNameModifierTest: React.FC = () => {
  const {createPortal} = useBakabaseContext();

  // 测试文件路径数据
  const testFilePaths = [
    'C:/Users/Documents/Photos/Vacation/IMG_001.jpg',
    'C:/Users/Documents/Photos/Vacation/IMG_002.jpg',
    'C:/Users/Documents/Photos/Vacation/IMG_003.jpg',
    'C:/Users/Documents/Photos/Vacation/IMG_004.jpg',
    'C:/Users/Documents/Photos/Vacation/IMG_005.jpg',
    'C:/Users/Documents/Videos/Movies/Action_Movie_2023.mp4',
    'C:/Users/Documents/Videos/Movies/Comedy_Movie_2023.mp4',
    'C:/Users/Documents/Videos/Movies/Drama_Movie_2023.mp4',
    'C:/Users/Documents/Music/Playlist/Rock_Song_01.mp3',
    'C:/Users/Documents/Music/Playlist/Pop_Song_02.mp3',
    'C:/Users/Documents/Music/Playlist/Jazz_Song_03.mp3',
    'C:/Users/Documents/Downloads/Software/Setup_Program_v1.0.exe',
    'C:/Users/Documents/Downloads/Software/Update_Patch_v1.1.exe',
    'C:/Users/Documents/Work/Reports/Quarterly_Report_2023_Q1.pdf',
    'C:/Users/Documents/Work/Reports/Annual_Report_2023.pdf',
    'C:/Users/Documents/Work/Presentations/Project_Presentation.pptx',
    'C:/Users/Documents/Backup/Important_Files_Backup.zip',
    'C:/Users/Documents/Backup/System_Backup_2023-12-01.zip',
    'C:/Users/Documents/Projects/Web_Development/index.html',
    'C:/Users/Documents/Projects/Web_Development/style.css',
    'C:/Users/Documents/Projects/Web_Development/script.js',
    'C:/Users/Documents/Projects/Mobile_App/Android/app.apk',
    'C:/Users/Documents/Projects/Mobile_App/iOS/app.ipa',
    'C:/Users/Documents/Projects/Desktop_App/Windows/app.exe',
    'C:/Users/Documents/Projects/Desktop_App/Mac/app.dmg',
    'C:/Users/Documents/Projects/Desktop_App/Linux/app.deb',
    'C:/Users/Documents/Projects/Data_Analysis/dataset.csv',
    'C:/Users/Documents/Projects/Data_Analysis/analysis_report.xlsx',
    'C:/Users/Documents/Projects/Data_Analysis/visualization.py',
    'C:/Users/Documents/Projects/Machine_Learning/model.pkl',
    'C:/Users/Documents/Projects/Machine_Learning/training_data.csv',
    'C:/Users/Documents/Projects/Machine_Learning/test_data.csv',
    'C:/Users/Documents/Projects/Database/schema.sql',
    'C:/Users/Documents/Projects/Database/data_backup.sql',
    'C:/Users/Documents/Projects/API/swagger_documentation.json',
    'C:/Users/Documents/Projects/API/endpoints.yaml',
    'C:/Users/Documents/Projects/Cloud_Deployment/docker-compose.yml',
    'C:/Users/Documents/Projects/Cloud_Deployment/kubernetes.yaml',
    'C:/Users/Documents/Projects/Cloud_Deployment/terraform.tf',
    'C:/Users/Documents/Projects/Security/ssl_certificate.crt',
    'C:/Users/Documents/Projects/Security/private_key.key',
    'C:/Users/Documents/Projects/Security/firewall_rules.conf',
    'C:/Users/Documents/Projects/Testing/unit_tests.js',
    'C:/Users/Documents/Projects/Testing/integration_tests.js',
    'C:/Users/Documents/Projects/Testing/e2e_tests.js',
    'C:/Users/Documents/Projects/Documentation/README.md',
    'C:/Users/Documents/Projects/Documentation/API_Docs.md',
    'C:/Users/Documents/Projects/Documentation/User_Guide.pdf',
    'C:/Users/Documents/Projects/Assets/logo.png',
    'C:/Users/Documents/Projects/Assets/icon.ico',
    'C:/Users/Documents/Projects/Assets/banner.jpg',
    'C:/Users/Documents/Projects/Config/config.json',
    'C:/Users/Documents/Projects/Config/environment.env',
    'C:/Users/Documents/Projects/Config/settings.yaml',
    'C:/Users/Documents/Projects/Logs/application.log',
    'C:/Users/Documents/Projects/Logs/error.log',
    'C:/Users/Documents/Projects/Logs/access.log',
    'C:/Users/Documents/Projects/Temp/temporary_file.tmp',
    'C:/Users/Documents/Projects/Temp/cache.dat',
    'C:/Users/Documents/Projects/Temp/session.ses',
    'C:/Users/Documents/Projects/Archive/old_version_1.0.zip',
    'C:/Users/Documents/Projects/Archive/legacy_code_2022.zip',
    'C:/Users/Documents/Projects/Archive/deprecated_features.zip',
    'C:/Users/Documents/Projects/Shared/libraries.dll',
    'C:/Users/Documents/Projects/Shared/frameworks.fw',
    'C:/Users/Documents/Projects/Shared/plugins.plg',
    'C:/Users/Documents/Projects/Production/live_system.exe',
    'C:/Users/Documents/Projects/Production/database.db',
    'C:/Users/Documents/Projects/Production/webserver.conf',
    'C:/Users/Documents/Projects/Staging/test_environment.exe',
    'C:/Users/Documents/Projects/Staging/test_database.db',
    'C:/Users/Documents/Projects/Staging/test_webserver.conf',
    'C:/Users/Documents/Projects/Development/dev_branch.exe',
    'C:/Users/Documents/Projects/Development/dev_database.db',
    'C:/Users/Documents/Projects/Development/dev_webserver.conf'
  ];

  return (
    <div style={{ padding: 24 }}>
      <h2>FileNameModifier</h2>
      <Button onPress={() => {
        createPortal(FileNameModifierModal, {
          onClose: () => {
            // 关闭模态框的处理逻辑
          },
          initialFilePaths: testFilePaths
        })
      }} variant="solid">
        Open with Test Files
      </Button>
    </div>
  );
};

export default FileNameModifierTest; 