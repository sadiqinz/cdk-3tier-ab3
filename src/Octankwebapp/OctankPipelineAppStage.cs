using Amazon.CDK;
using Constructs;

namespace Octankwebapp
{
    class OctankPipelineAppStage : Stage
    {
        public OctankPipelineAppStage(Construct scope, string id, StageProps props=null) : base(scope, id, props)
        {
            Stack OctankWebApp = new OctankwebappStack(this, "OctankWebAppStack");
        }
    }
}