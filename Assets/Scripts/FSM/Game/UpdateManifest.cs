using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.FSM
{
    /// <summary>服务端返回的更新清单</summary>
    [Serializable]
    public class UpdateManifest
    {
        /// <summary>远程版本号（x.y.z 格式）</summary>
        public string Version;

        /// <summary>是否强制更新（整包下载，不热更）</summary>
        public bool ForceUpdate;

        /// <summary>更新说明（展示给用户）</summary>
        public string ReleaseNotes;

        /// <summary>需要更新的文件列表</summary>
        public List<UpdateFileInfo> Files = new List<UpdateFileInfo>();

        /// <summary>热更资源根 URL（与文件 name 拼接即完整下载地址）</summary>
        public string BaseDownloadUrl;
    }

    /// <summary>单个更新文件信息</summary>
    [Serializable]
    public class UpdateFileInfo
    {
        /// <summary>文件相对路径（如 "bundles/data.bundle"）</summary>
        public string Name;

        /// <summary>文件大小（字节）</summary>
        public long Size;

        /// <summary>MD5 校验值</summary>
        public string MD5;

        /// <summary>目标存放子目录（相对于 persistentDataPath）</summary>
        public string SubDir;
    }
}
