#!/bin/bash
# ======================================================
# ZWebGLFrameWork — Unity 命令行编译 & 验证脚本
# 用法: ./ci_build.sh [WebGL|Windows|Android|iOS|Validate]
# 示例: ./ci_build.sh Validate   # 仅编译检查
#       ./ci_build.sh WebGL      # 编译 + WebGL 打包
# ======================================================

set -e

UNITY_PATH="${UNITY_PATH:-/Applications/Unity/Hub/Editor/2022.3.*/Unity.app/Contents/MacOS/Unity}"
PROJECT_PATH="$(cd "$(dirname "$0")" && pwd)"
BUILD_MODE="${1:-Validate}"
LOG_FILE="$PROJECT_PATH/BuildLogs/${BUILD_MODE}_$(date +%Y%m%d_%H%M%S).log"

mkdir -p "$PROJECT_PATH/BuildLogs"

echo "===== ZWebGLFrameWork CI Build ====="
echo "Mode:    $BUILD_MODE"
echo "Project: $PROJECT_PATH"
echo "Log:     $LOG_FILE"
echo ""

if [ "$BUILD_MODE" == "Validate" ]; then
    echo "[CI] 编译验证模式（不打包）..."
    $UNITY_PATH \
        -batchmode \
        -quit \
        -projectPath "$PROJECT_PATH" \
        -executeMethod ZWebGLFrameWork.Editor.BuildValidator.CompileAndValidate \
        -logFile "$LOG_FILE" \
        -nographics

elif [ "$BUILD_MODE" == "WebGL" ]; then
    echo "[CI] WebGL 构建..."
    BUILD_DIR="$PROJECT_PATH/Builds/WebGL"
    mkdir -p "$BUILD_DIR"
    $UNITY_PATH \
        -batchmode \
        -quit \
        -projectPath "$PROJECT_PATH" \
        -buildTarget WebGL \
        -executeMethod ZWebGLFrameWork.Editor.BuildScript.BuildWebGL \
        -logFile "$LOG_FILE" \
        -nographics

elif [ "$BUILD_MODE" == "Windows" ]; then
    echo "[CI] Windows 构建..."
    BUILD_DIR="$PROJECT_PATH/Builds/Windows"
    mkdir -p "$BUILD_DIR"
    $UNITY_PATH \
        -batchmode \
        -quit \
        -projectPath "$PROJECT_PATH" \
        -buildTarget StandaloneWindows64 \
        -executeMethod ZWebGLFrameWork.Editor.BuildScript.BuildWindows \
        -logFile "$LOG_FILE"

elif [ "$BUILD_MODE" == "Android" ]; then
    echo "[CI] Android 构建..."
    BUILD_DIR="$PROJECT_PATH/Builds/Android"
    mkdir -p "$BUILD_DIR"
    $UNITY_PATH \
        -batchmode \
        -quit \
        -projectPath "$PROJECT_PATH" \
        -buildTarget Android \
        -executeMethod ZWebGLFrameWork.Editor.BuildScript.BuildAndroid \
        -logFile "$LOG_FILE"

elif [ "$BUILD_MODE" == "iOS" ]; then
    echo "[CI] iOS 构建..."
    BUILD_DIR="$PROJECT_PATH/Builds/iOS"
    mkdir -p "$BUILD_DIR"
    $UNITY_PATH \
        -batchmode \
        -quit \
        -projectPath "$PROJECT_PATH" \
        -buildTarget iOS \
        -executeMethod ZWebGLFrameWork.Editor.BuildScript.BuildIOS \
        -logFile "$LOG_FILE"

else
    echo "[CI] 未知模式: $BUILD_MODE"
    echo "可用: Validate | WebGL | Windows | Android | iOS"
    exit 1
fi

EXIT_CODE=$?

# 检查编译结果
if [ $EXIT_CODE -eq 0 ]; then
    echo ""
    echo "===== ✅ 构建成功 ====="
else
    echo ""
    echo "===== ❌ 构建失败 (exit=$EXIT_CODE) ====="
    echo "日志: $LOG_FILE"
    grep -i "error" "$LOG_FILE" | tail -20
fi

exit $EXIT_CODE
