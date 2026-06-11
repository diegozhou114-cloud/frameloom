using System;

namespace ZWebGLFrameWork.Data
{
    /// <summary>玩家核心数据</summary>
    public class PlayerData : ObservableModel
    {
        /// <summary>玩家昵称</summary>
        public Observable<string> Name = new Observable<string>("Villager");

        /// <summary>等级</summary>
        public Observable<int> Level = new Observable<int>(1);

        /// <summary>经验值</summary>
        public Observable<int> Exp = new Observable<int>(0);

        /// <summary>金币</summary>
        public Observable<int> Gold = new Observable<int>(500);

        /// <summary>圣水</summary>
        public Observable<int> Elixir = new Observable<int>(300);

        /// <summary>暗黑重油</summary>
        public Observable<int> DarkElixir = new Observable<int>(0);

        /// <summary>宝石（付费货币）</summary>
        public Observable<int> Gems = new Observable<int>(50);

        /// <summary>奖杯数</summary>
        public Observable<int> Trophies = new Observable<int>(0);

        /// <summary>大本营等级</summary>
        public Observable<int> TownHallLevel = new Observable<int>(1);

        /// <summary>经验值到下一级的阈值</summary>
        public int ExpToNextLevel => Level.Value * 100;
    }
}
