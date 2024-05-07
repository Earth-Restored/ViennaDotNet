@ECHO OFF

if not exist "build" mkdir build

if not exist "build\ApiServer" mkdir build\ApiServer
robocopy ViennaDotNet.ApiServer\bin\Release\net8.0\publish build\ApiServer ApiServer.exe /njh /njs /nc /ns /np
robocopy ViennaDotNet.ApiServer\bin\Release\net8.0\publish build\ApiServer aspnetcorev2_inprocess.dll /njh /njs /nc /ns /np
robocopy ViennaDotNet.ApiServer\bin\Release\net8.0\publish build\ApiServer e_sqlite3.dll /njh /njs /nc /ns /np
if not exist "build\ApiServer\data" mkdir build\ApiServer\data
robocopy ViennaDotNet.ApiServer\bin\Release\net8.0\publish\data build\ApiServer\data /e /njh /njs /nc /ns /np /nfl
del "build\ApiServer\data\resourcepacks\vanilla.zip" >nul 2>&1


if not exist "build\Buildplate" mkdir build\Buildplate
robocopy ViennaDotNet.Buildplate\bin\Release\net8.0\publish build\Buildplate BuildplateLauncher.exe /njh /njs /nc /ns /np
robocopy ViennaDotNet.Buildplate\bin\Release\net8.0\publish build\Buildplate e_sqlite3.dll /njh /njs /nc /ns /np
if not exist "build\Buildplate\registry" mkdir build\Buildplate\registry
robocopy ViennaDotNet.Buildplate\bin\Release\net8.0\publish\registry build\Buildplate\registry /e /njh /njs /nc /ns /np /nfl

if not exist "build\Buildplate_Importer" mkdir build\Buildplate_Importer
robocopy ViennaDotNet.Buildplate_Importer\bin\Release\net8.0\publish build\Buildplate_Importer ViennaDotNet.Buildplate_Importer.exe /njh /njs /nc /ns /np
robocopy ViennaDotNet.Buildplate_Importer\bin\Release\net8.0\publish build\Buildplate_Importer e_sqlite3.dll /njh /njs /nc /ns /np


if not exist "build\EventBusServer" mkdir build\EventBusServer
robocopy ViennaDotNet.EventBus.Server\bin\Release\net8.0\publish build\EventBusServer EventBusServer.exe /njh /njs /nc /ns /np

if not exist "build\ObjectStoreServer" mkdir build\ObjectStoreServer
robocopy ViennaDotNet.ObjectStore.Server\bin\Release\net8.0\publish build\ObjectStoreServer ObjectStoreServer.exe /njh /njs /nc /ns /np

if not exist "build\TappablesGenerator" mkdir build\TappablesGenerator
robocopy ViennaDotNet.TappablesGenerator\bin\Release\net8.0\publish build\TappablesGenerator TappablesGenerator.exe /njh /njs /nc /ns /np
if not exist "build\TappablesGenerator\data" mkdir build\TappablesGenerator\data
if not exist "build\TappablesGenerator\data\tappable" mkdir build\TappablesGenerator\data\tappable
robocopy ViennaDotNet.TappablesGenerator\bin\Release\net8.0\publish\data\tappable build\TappablesGenerator\data\tappable /e /njh /njs /nc /ns /np /nfl

robocopy  ViennaDotNet.Launcher\bin\Release\net8.0\publish build ViennaDotNet.Launcher.exe /njh /njs /nc /ns /np

if not exist "build\MCServer" mkdir build\MCServer
if not exist "build\MCServer\mods" mkdir build\MCServer\mods

if not exist "build\FountainBridge" mkdir build\FountainBridge