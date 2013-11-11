using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Vydejna.Web
{
    public class ServiceHttpHandler : IHttpHandler
    {
        private object _service;

        public ServiceHttpHandler(object service)
        {
            _service = service;
        }

        public bool IsReusable
        {
            get { return true; }
        }

        public void ProcessRequest(HttpContext context)
        {
            var methodName = context.Request.Url.Segments.LastOrDefault();
            if (methodName == null)
                throw new HttpException(404, "Action not specified");
            var methodInfo = _service.GetType().GetMethod(methodName);
            if (methodInfo == null)
                throw new HttpException(404, "Action not found");
            var inputType = methodInfo.GetParameters()[0].ParameterType;
            var outputType = methodInfo.ReturnType;
            var inputObject = Activator.CreateInstance(inputType);
            var outputObject = methodInfo.Invoke(_service, new object[] { inputObject });
            context.Response.ContentType = "application/xml";
            context.Response.Write(outputObject.ToString());
        }
    }
}