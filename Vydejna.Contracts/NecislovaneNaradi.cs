using ServiceLib;
using System;

namespace Vydejna.Contracts
{
    #region Soucasti udalosti
    public class SkupinaNecislovanehoNaradiDto
    {
        public DateTime Datum { get; set; }
        public decimal Cena { get; set; }
        public string Cerstvost { get; set; }
        public int Pocet { get; set; }

        public override int GetHashCode()
        {
            return DtoUtils.GetHashCode(this);
        }
        public override bool Equals(object obj)
        {
            return DtoUtils.Equals(this, obj);
        }
        public override string ToString()
        {
            return DtoUtils.ToString(this);
        }
    }
    #endregion
}
