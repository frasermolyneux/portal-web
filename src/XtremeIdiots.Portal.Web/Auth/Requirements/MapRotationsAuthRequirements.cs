using Microsoft.AspNetCore.Authorization;

namespace XtremeIdiots.Portal.Web.Auth.Requirements;

public class AccessMapRotations : IAuthorizationRequirement { }
public class ManageMapRotations : IAuthorizationRequirement { }
public class CreateMapRotation : IAuthorizationRequirement { }
public class EditMapRotation : IAuthorizationRequirement { }
public class DeleteMapRotation : IAuthorizationRequirement { }
