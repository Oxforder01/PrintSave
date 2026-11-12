using System;
using PrintSave.Core;

namespace PrintSave.Conversion
{
    /// <summary>
    /// Dispatches a spool file to the first registered converter that handles its format.
    /// Lets the capture engine hold a single IDocumentConverter while supporting many formats.
    /// </summary>
    public sealed class CompositeConverter : IDocumentConverter
    {
        private readonly IDocumentConverter[] _converters;

        public CompositeConverter(params IDocumentConverter[] converters)
        {
            _converters = converters ?? new IDocumentConverter[0];
        }

        public bool CanConvert(string format)
        {
            foreach (IDocumentConverter c in _converters)
                if (c.CanConvert(format)) return true;
            return false;
        }

        public bool TryConvertToPdf(string inputSpoolPath, string outputPdfPath, string format, out string error)
        {
            foreach (IDocumentConverter c in _converters)
                if (c.CanConvert(format))
                    return c.TryConvertToPdf(inputSpoolPath, outputPdfPath, format, out error);

            error = "no converter registered for format " + format;
            return false;
        }
    }
}
